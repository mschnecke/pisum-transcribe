# Notes

## Hardware verification (tasks 5.1 and 5.2), 2026-09-17

**Machine:** Lenovo 21SX (ThinkPad E14 Gen 7), Intel Core Ultra 7 255H, Intel Arc 140T iGPU, transcribe.cpp 0.2.3.

**Audio:** synthetic speech from the Windows SAPI voices, 16 kHz mono. German clip 10.7 s (Microsoft Hedda), English clip 10.5 s (Microsoft Zira). Real recordings may decode differently.

### Integration tests (5.1)

All three pass with the default model `canary-1b-v2-q8_0`, which the `auto` backend loaded on Vulkan.

Output checked by hand (TTS clips, so no private text):

| Request | canary-1b-v2-q8_0 on Vulkan |
|---|---|
| de→en translate | Good morning everyone. The new version has been finalized and will be delivered to all customers on Friday. Please contact us for any questions. |
| de transcribe (target setting `en`) | Guten Morgen zusammen. Die neue Version ist fertig getestet und wird am Freitag an alle Kunden ausgeliefert. Bitte meldet euch bei Fragen. |
| en→de translate | Guten Morgen, alle. Die neue Version wurde vollständig getestet und wird am Freitag an alle Kunden versandt. Bitte lassen Sie mich wissen, wenn Sie noch offene Fragen haben. |
| en transcribe | Good morning everyone. The new release has been fully tested and will be shipped to all customers on Friday. Please let me know if you have any open questions. |

en→de translation works through `WithTask(Translate)` and `WithTargetLanguage`, so the risk in design.md does not apply. Passing the source language as the target for `transcribe` keeps the German text.

### Benchmark (5.2)

de→en translate on the 10.7 s German clip, median of 5 warm runs. Three benchmark runs: the first with the original 1 s silence warm-up, then two after task 3.7 with the D5 warm-up input (Vulkan: 10 s of noise; CPU: 1 s of silence). Each cell lists the runs in that order.

| Model | Backend | Load | Warm-up, silence (first run) | Warm-up, D5 input (reruns) | Warm median |
|---|---|---|---|---|---|
| canary-1b-v2-q8_0 | Vulkan | 3.84 / 3.56 / 2.34 s | 1.14 s | 1.04 / 0.80 s | 1.94 / 1.72 / 1.25 s |
| canary-1b-v2-q8_0 | CPU | 3.55 / 4.04 / 3.20 s | 1.39 s | 1.21 / 0.60 s | 15.34 / 12.79 / 10.77 s |
| canary-180m-flash-q8_0 | Vulkan | 0.82 / 1.02 / 0.74 s | 0.18 s | 0.51 / 0.46 s | 1.26 / 1.29 / 0.88 s |
| canary-180m-flash-q8_0 | CPU | 0.68 / 0.81 / 0.60 s | 0.41 s | 7.02 / 0.63 s | 4.42 / 2.37 / 12.01 s |

The warm-up cache was warm in all three runs, so the reruns show the steady cost of the D5 input: about 0.3 s more than silence on the 180M model. For the 1B model on Vulkan, the reruns were not slower than the silence run; the variation between runs is larger than the difference. The 7.02 s CPU warm-up of the 180M model did not repeat. CPU medians vary widely between runs on this laptop (2.37–12.01 s for the 180M model).

Not flagged: the warm Vulkan median is below 3 s and faster than CPU for both models. Vulkan stays the default backend and `canary-1b-v2-q8_0` the default model.

`canary-1b-v2-q4_k_m` was not installed and is not in the table.

### Observations

- **CPU fallback is slow with the 1B model.** 15.3 s for 10.7 s of audio is slower than real time. With `auto`, a Vulkan failure (D4, D10) leaves the user with this latency.
- **The 1 s silence warm-up leaves GPU pipeline compilation to the first real request** when the driver shader cache is cold. See "Warm-up input" below, which led to the D5 revision.
- **Diagnostic messages:** `dotnet test` does not print `TestContext.SendDiagnosticMessage` output. Run the test executable with `-diagnostics` to see the benchmark table.

## Warm-up input (D5 revision), 2026-09-17

Scratch programs on TranscribeCppSharp directly, same machine and German TTS clip. Each program loads the model, runs the warm-up (transcribe `en`), then three de→en translations of the 10.7 s clip ("first", "later"). A program built under a new file name starts with a cold driver shader cache; running it again uses the warm cache.

**Warm-up inputs:**
- `silence1`: 1 s of silence.
- `silence10`: 10 s of silence.
- `noise10`: 10 s of pseudo-random noise, uniform in [-0.1, 0.1], fixed seed.
- `speech`: the German clip itself.

### canary-1b-v2-q8_0 on Vulkan, cold cache

| Warm-up | Samples | Warm-up | First run | Later runs |
|---|---|---|---|---|
| silence1 | 5 | 14.2–21.2 s | 3.6–5.8 s | 1.2–2.3 s |
| silence10 | 1 | 18.9 s | 1.4 s | 1.2–2.1 s |
| noise10 | 6 | 14.6–19.6 s, one outlier of 130 s | 1.6–2.5 s | 1.2–2.7 s |
| speech | 1 | 24.6 s | 2.0 s | 1.6–2.0 s |

The 130 s warm-up did not repeat in two further cold runs (14.6 s and 15.0 s).

### canary-1b-v2-q8_0 on Vulkan, warm cache

| Warm-up | Samples | Warm-up | Warm-up text | First run | Later runs |
|---|---|---|---|---|---|
| silence1 | 4 | 0.6–1.5 s | 5 chars | 1.2–3.0 s | 1.0–5.6 s |
| silence10 | 4 | 4.6–5.4 s | 165 chars | 1.8–2.6 s | 1.4–2.4 s |
| noise10 | 5 | 1.3–1.5 s | 0 chars | 1.4–3.6 s | 1.3–3.2 s |
| speech | 4 | 2.6–3.1 s | 146 chars | 2.0–2.5 s | 1.2–2.5 s |

With a warm cache, the first run averages about 0.4 s above later runs for every input, including the speech clip itself. The spread between runs is larger than that.

### Other models and backends, one sample each

| Model | Backend | Warm-up | Warm-up time | Warm-up text | First run | Later runs |
|---|---|---|---|---|---|---|
| canary-180m-flash-q8_0 | Vulkan | silence1 (cold) | 15.6 s | 5 chars | 3.6 s | 0.7–1.0 s |
| canary-180m-flash-q8_0 | Vulkan | noise10 | 0.5 s | 0 chars | 0.9 s | 0.5–0.6 s |
| canary-180m-flash-q8_0 | CPU | silence1 | 0.2 s | 5 chars | 2.5 s | 1.7–3.1 s |
| canary-180m-flash-q8_0 | CPU | noise10 | 1.2 s | 0 chars | 2.0 s | 1.8–2.3 s |
| canary-1b-v2-q8_0 | CPU | silence1 | 2.2 s | 5 chars | 14.0 s | 11.6–14.9 s |
| canary-1b-v2-q8_0 | CPU | noise10 | 12.5 s | 0 chars | 35.2 s | 34.8–55.0 s |

The 1B CPU runs after the noise warm-up were much slower. They ran right after the silence runs, and the cause (possibly heat) was not investigated. It does not affect D5, because CPU keeps 1 s of silence.

### Conclusions

- On Vulkan with a cold cache, 10 s of noise moves the pipeline compilation from the first real run into the warm-up.
- 10 s of silence also does, but the 1B model produces text on it, which costs about 4 s of decoding on every start.
- Noise produces no text on either model and costs about 0.5 s more than 1 s of silence once the cache is warm.
- A speech clip is not better than noise.
- The CPU backend gains nothing from a longer warm-up.
