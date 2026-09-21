# Notes

## Long clips on Vulkan (tasks 1.2 and 1.3), 2026-09-21

**Machine:** Lenovo 21SX (ThinkPad E14 Gen 7), Intel Core Ultra 7 255H, Intel Arc 140T iGPU (UMA, reported as 31 GB of shared memory, driver 32.0.101.8826), 64 GB RAM, transcribe.cpp 0.2.3. The app was not running. Other programs used about 2.2 GB of the shared GPU memory.

**Audio:** the German TTS clip from `add-transcription-engine` (10.7 s, Microsoft Hedda, 16 kHz mono), repeated to each length. Model `canary-1b-v2-q8_0`, de→en translate.

**Test:** `Run_LongClipsOnVulkan_ReportsOutcomeAndReload`, run with `Pisum.Transcribe.Tests.exe -explicit only -method "*.Run_LongClipsOnVulkan_ReportsOutcomeAndReload" -diagnostics`, once without a limit and three times with a forced limit set in the environment of the test process.

### Results

Each cell is the outcome and the time until `Run` returned. For a completed clip, that time is its processing time. No output was truncated.

| Step | No limit | `ALLOCATION_SIZE` 64 MiB | `ALLOCATION_SIZE` and `BUFFER_SIZE` 64 MiB | `ALLOCATION_SIZE` and `BUFFER_SIZE` 32 MiB |
|---|---|---|---|---|
| Warm-up input (10 s) | completed, 0.51 s | completed, 0.93 s | completed, 1.69 s | completed, 0.95 s |
| 30 s | completed, 1.93 s | completed, 2.17 s | completed, 2.77 s | completed, 3.54 s |
| 60 s | completed, 4.12 s | completed, 4.34 s | completed, 4.06 s | completed, 8.89 s |
| 120 s | completed, 6.70 s | completed, 9.76 s | completed, 21.75 s | completed, 28.72 s |
| 240 s | completed, 41.78 s | completed, 43.65 s | completed, 78.50 s | **failed: `ErrOom`, 117.38 s** |
| 399 s | completed, 173.51 s | completed, 177.62 s | **failed: `ErrOom`, 234.26 s** | not run |
| Same engine after the failure, warm-up input | not run | not run | completed, 0.70 s | completed, 0.73 s |
| Reload on Vulkan, warm-up input | not run | not run | loaded in 1.50 s, completed in 0.79 s | loaded in 1.28 s, completed in 0.76 s |
| `MaxAudio` on Vulkan | 400 s | 400 s | 400 s | 400 s |
| `MaxAudio` on the CPU | 400 s | 400 s | 400 s | 400 s |

`ALLOCATION_SIZE` is `GGML_VK_FORCE_MAX_ALLOCATION_SIZE`, `BUFFER_SIZE` is `GGML_VK_FORCE_MAX_BUFFER_SIZE`, both in bytes (64 MiB = 67108864, 32 MiB = 33554432).

### Findings

- **No clip runs out of memory on this laptop.** Without a limit, every clip up to 399 s completes on Vulkan. The premise of #12, that long clips run out of GPU memory on the target laptop, doesn't hold here.
- **`GGML_VK_FORCE_MAX_ALLOCATION_SIZE` alone has no effect.** With 64 MiB, every clip completes, 0.2–4.1 s slower than without a limit. The failure appears only with `GGML_VK_FORCE_MAX_BUFFER_SIZE` set too. ggml-vulkan then logs `Requested buffer size exceeds device buffer size limit: ErrorOutOfDeviceMemory`, and transcribe.cpp returns `ErrOom`. Both variables were set to the same value, so the weights are split into buffers below the cap; a run with only `GGML_VK_FORCE_MAX_BUFFER_SIZE` was not tried.
- **The buffer that fails is the cross-attention KV cache, not a compute buffer of the encoder.** transcribe.cpp logs `canary run: KV cache allocation failed (n_ctx=1024, T_enc=…) — out of memory`. The buffer takes 16,384 bytes per encoder frame, and a frame is 80 ms of audio: 49,168,384 bytes for 240 s (3001 frames) and 81,723,392 bytes for 399 s (4988 frames). So a cap of 64 MiB fails clips above about 328 s (4096 frames), and 32 MiB fails clips above about 164 s (2048 frames). No tensor of the encoder exceeded the cap, up to 240 s with 32 MiB and up to 399 s with 64 MiB, so the attention score matrix that the #9 estimate expected to fail first is not the limit.
- **The failed run returns only after the whole encoder pass.** The KV cache is allocated once the encoder has produced its frames, so the failed Vulkan attempt costs its full encoder time: 117 s for 240 s of audio and 234 s for 399 s, under the caps. The caps also slow the encoder down (120 s: 6.7 s without a limit, 21.8 s and 28.7 s with the caps), so these times overstate what a failed attempt costs on a GPU that really has less memory. They still show that the failure comes at the end of the encoder, not at its start. That answers the open question in `design.md`: the failed attempt is not cheap.
- **After the failure, the engine keeps working.** The same engine runs the warm-up input, and loading the model on Vulkan again takes 1.3–1.5 s plus 0.8 s of warm-up.
- **`MaxAudio` is 400 s on both backends,** so task 2.1 needs no smaller limit during the return.
- **Times vary a lot between runs:** 6.7–28.7 s for 120 s and 41.8–78.5 s for 240 s. The capped runs were slower, and it wasn't checked how much of that comes from the caps and how much from heat.

### Checkpoint (task 1.3)

**Decision: continue with group 2.** It matches the row "Every clip completes up to 399 s" of the checkpoint table in `design.md` (D1). With a forced limit, a clip failed with `ErrOom`, and the reload on Vulkan ran the 10 s input. No clip failed with `ErrBackend`, the process didn't crash, and the reload worked. The change therefore serves GPUs with less memory. `MaxAudio` is the same on both backends, so task 2.1 skips the smaller limit.

The forced limit needed `GGML_VK_FORCE_MAX_BUFFER_SIZE` as well as the `GGML_VK_FORCE_MAX_ALLOCATION_SIZE` that the design named. The user chose to continue after seeing this and the cost of a failed attempt. D1, the risks and the open question in `design.md` and hand check 3.2 were corrected to match. The hand check uses 32 MiB, so a dictation longer than 164 s fails on Vulkan.

## Hand check (task 3.2), 2026-09-21

Passed, as reported by the user, with `GGML_VK_FORCE_MAX_BUFFER_SIZE` and `GGML_VK_FORCE_MAX_ALLOCATION_SIZE` set to 33554432 (32 MiB), backend `auto` and the default model.
