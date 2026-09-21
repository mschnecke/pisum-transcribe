# Building a Windows-Only Push-to-Talk Dictation App in .NET with Canary-1b-v2 + transcribe.cpp

## TL;DR
- **Yes, a .NET (C#) app is entirely practical.** A .NET binding to transcribe.cpp already exists (`TranscribeCppSharp`, MIT, on NuGet), NVIDIA Canary-1b-v2 does German→English speech translation, and the whole Windows desktop stack (WASAPI capture, global hotkeys, text injection, tray UI, auto-update) is mature in .NET. The hard part is not "can it be done" but "who maintains the native binding against a fast-moving 0.x C++ library."
- **Recommended architecture:** an in-process P/Invoke path via TranscribeCppSharp (or your own vendored copy of its interop), packaging the CPU+Vulkan `transcribe.dll`/`ggml*.dll` per-RID, with a WPF + H.NotifyIcon tray shell, SharpHook for hold-to-talk, Silero VAD to trim silence, and clipboard/SendInput for pasting. Keep a warm `Model`+`Session` alive across invocations.
- **Biggest risks:** transcribe.cpp is v0.2.x with a non-frozen ABI (only 40 GitHub stars / 2 forks as of Sept 2026) and a documented concurrency limitation; the existing C# wrapper is very young (1 star) and still pinned to an older upstream (0.1.3). Have a fallback — sherpa-onnx's official C# bindings also support Canary + translation — and budget for maintaining native CI.

## Key Findings

1. **transcribe.cpp exposes a single C header** (`include/transcribe.h`), is MIT-licensed, built on ggml, and supports Metal/Vulkan/CUDA/CPU backends. It ships official Python, TypeScript, Rust and Swift bindings and a `transcribe-cli`. The API is **fully synchronous/blocking** (no async entry points). It is the engine behind CJ Pais's Handy dictation app.
2. **A .NET binding already exists:** `manuc66/TranscribeCppSharp` (MIT), on NuGet as `TranscribeCppSharp` plus per-RID native packages (`TranscribeCppSharp.Native.win-x64`). It uses modern `LibraryImport` interop and `SafeHandle`, ships **CPU + Vulkan** binaries for Windows, and mirrors the Whisper.net-style `runtimes/<rid>/native` packaging. This is close to a drop-in for the user's need — but it currently binds an older upstream version and is early-stage.
3. **Canary-1b-v2 does exactly what the user wants.** Per NVIDIA's Interspeech 2026 paper (Sekoyan et al., arXiv:2509.14128), it is "a fast, robust multilingual model for Automatic Speech Recognition (ASR) and Speech-to-Text Translation (AST)… it supports 25 European languages… trained on 1.7M hours of total data samples." German→English is `--task translate -sl de -tl en`. No auto-detect (language hint mandatory), no streaming.
4. **Vulkan on the target hardware is viable but must be validated.** The Vulkan loader (`vulkan-1.dll`) ships with modern Intel GPU drivers on Windows 11, so no separate runtime install is normally needed on end-user machines. The Vulkan SDK is only needed at build time.
5. **The surrounding Windows stack is well-supported in .NET:** NAudio/WASAPI for 16 kHz mono float capture, SharpHook for hold/release hotkeys, clipboard+Ctrl+V / SendInput Unicode for pasting, WPF + H.NotifyIcon for a tray app, Velopack for auto-update + WinGet/Chocolatey.

## Details

### 1. transcribe.cpp C API and .NET interop

`transcribe.cpp` (github.com/handy-computer/transcribe.cpp) is MIT-licensed and organizes its public surface as a **single C header, `include/transcribe.h`**. It vendors ggml (MIT) and miniz (MIT). Upstream is at **v0.2.3**; v0.2.0 was a breaking ABI change (device selection moved from integer `gpu_device` indices to opaque device handles). **The ABI is explicitly 0.x and not frozen** — the repo has only 40 stars and 2 forks as of September 2026, confirming its early-stage status — and the header carries a documented "KNOWN 0.x LIMITATION — concurrent COMPUTE" note.

Confirmed API facts (from the header, the Rust/Python bindings, and the C# wrapper):
- **Backend init:** `transcribe_init_backends` must be called once before loading a model.
- **Model lifecycle:** load-from-GGUF, then `transcribe_model_free`. The model must outlive all its sessions.
- **Session lifecycle:** create session from model, run (blocking), free session before model.
- **Run controls are string-based**, using a `"default"` sentinel to preserve each model family's shipped behavior; `lang` is a string (e.g. `"de"`), and `pnc`/`itn` are string toggles. Translation is supported; the exact representation of source-lang/target-lang/task in the C struct should be read directly from the header before finalizing bindings.
- **Result** carries full text plus segments/words/tokens/timings; strings are UTF-8 `char*` by ggml convention.
- **Cancellation** is via a separate `cancel()` call from another thread (the run returns an `Aborted` status with a partial result), not an abort callback. Presence of progress/segment callbacks is unconfirmed — verify in the header.
- **Status enum** includes `Ok`, `ErrGguf`, `ErrBackend`, `ErrInvalidArg`, plus `Aborted` and an output-truncated state; the family docs also reference `TRANSCRIBE_ERR_INPUT_TOO_LONG` for over-long audio.

**Interop approach.** You do **not** need to hand-write P/Invoke: TranscribeCppSharp's `TranscribeCppSharp.Interop` project contains auto-generated `LibraryImport` declarations that mirror the C ABI, and it registers a `DllImportResolver` to find `transcribe.dll`/`libtranscribe.so` in the app output directory or the NuGet native package layout. If you prefer to own the binding, copy that interop project (MIT) into your solution and regenerate against the exact upstream tag you ship. For a brand-new binding, the modern .NET 8+ path is the `LibraryImport` source generator (compile-time marshalling, NativeAOT-friendly) rather than classic `DllImport`; a thin C shim DLL or C++/CLI is unnecessary because transcribe.cpp already presents a flat C ABI.

**Marshalling considerations.** Pass audio as `float[]` (16 kHz mono, normalized to [-1, 1]) — with `LibraryImport` this marshals as a pinned pointer + length. Receive UTF-8 strings with `[MarshalUsing(typeof(Utf8StringMarshaller))]` (or read the `byte*` and `Encoding.UTF8.GetString`). Wrap the native model/session handles in `SafeHandle` subclasses and dispose explicitly (`using`), because finalizer ordering is not guaranteed and the model must outlive sessions. If callbacks exist, marshal them with `[UnmanagedCallersOnly]` static methods and keep a GC handle alive for the duration of the call.

### 2. Whisper.net as the packaging template

`Whisper.net` (1.9.x) is the reference example of how to do this well in .NET, and its packaging model is directly reusable:
- The core `Whisper.net` package is managed-only; native binaries ship in separate per-backend runtime packages: `Whisper.net.Runtime` (CPU), `Whisper.net.Runtime.Vulkan`, `Whisper.net.Runtime.Cuda`, `.CoreML`, `.OpenVino`, `.NoAvx`, and a `Whisper.net.AllRuntimes` meta-package.
- **A Vulkan runtime NuGet package exists** — `Whisper.net.Runtime.Vulkan` (1.9.x; "Windows x64 with Vulkan installed") — proving the Vulkan-on-Windows path is real and packaged in the .NET ecosystem.
- A `NativeLibraryLoader` probes runtimes in a configurable order (`RuntimeLibraryOrder`), and you can drop in your own native builds under `./runtimes/<rid>/native` and it will find them.
- API shape: `WhisperFactory` → processor builder → async streaming of segments (`await foreach`). Licensed MIT.
- TranscribeCppSharp deliberately mirrors this layout, so the same MSBuild/NuGet conventions carry over to a transcribe.cpp wrapper.

Note the CPU caveat inherited from whisper.cpp: x64 builds require AVX/AVX2/FMA/F16C. The ThinkPad E14 Gen 7's modern Intel CPU has these.

**Alternatives to Whisper.net:** `Const-me/Whisper` is a DirectCompute/D3D11 C# wrapper (Windows-only, very fast on any D3D11 GPU, but Whisper-only and not maintained for Canary). `WhisperCpp.NET` is another wrapper but less active. None of these run Canary — for Canary specifically you need transcribe.cpp or sherpa-onnx (see §10).

### 3. Building transcribe.cpp for Windows with Vulkan, and shipping it

Per the upstream `docs/build-windows.md`:
- Toolchain: Visual Studio Build Tools 2022 (MSVC 19.44 / toolset 14.44 tested), CMake 4.x. The Visual Studio generator is multi-config, so pass `--config Release`.
- Vulkan build: install the Vulkan SDK (`winget install --id KhronosGroup.VulkanSDK`), which provides `glslc`, headers and `vulkan-1.lib` and sets the machine-wide `VULKAN_SDK` variable. Then:
  ```
  cmake -B build -DTRANSCRIBE_VULKAN=ON
  cmake --build build --target transcribe-cli --config Release
  ```
  A successful configure prints `Found Vulkan: ... found components: glslc` and `Including Vulkan backend`. ggml builds a `vulkan-shaders-gen` helper as a nested ExternalProject (transcribe.cpp flattens it on Windows). On the target machine it auto-selects the iGPU, e.g. `backend: Vulkan0 ("Intel(R) Iris(R) Xe Graphics")`.
- **Resulting DLLs:** `transcribe.dll` plus sibling `ggml*.dll` files (`ggml.dll`, `ggml-base.dll`, `ggml-cpu.dll`, `ggml-vulkan.dll`). For the wrapper to load a custom build, copy `transcribe.dll` **and its sibling ggml DLLs** into the app output directory; the loader prefers app-directory binaries over packaged ones. Build with `-DBUILD_SHARED_LIBS=ON`.
- **Bundling in .NET:** use the `runtimes/win-x64/native/` convention inside a NuGet package (as Whisper.net does), or an MSBuild `<None>`/`<Content>` copy target to place the DLLs next to the exe. `NativeLibrary.SetDllImportResolver` (already done by TranscribeCppSharp) resolves the entry DLL; the OS loader then resolves the ggml siblings from the same directory.
- **Static linking** is possible to reduce DLL sprawl but is not the documented happy path for the Vulkan build; the ggml backend model favors shared libraries. For a first ship, keep the DLLs side-by-side.
- **Vulkan runtime dependency on end users:** the Vulkan **loader** (`vulkan-1.dll`) ships with modern GPU drivers on Windows 11 (Intel/AMD/NVIDIA), so a machine with up-to-date Intel Xe/Arc drivers already has it. You do not ship the SDK. Defensive UX: detect the loader/driver at startup and fall back to the CPU backend if Vulkan device creation fails (the Vulkan loader can be broken by stale third-party overlay layers — a known Intel Xe failure mode where `vkCreateDevice` fails with `ERROR_DEVICE_LOST`).
- **First-run SPIR-V/shader cost:** ggml compiles/warms its Vulkan pipelines on first use, adding latency to the first transcription of a session. Warm it by running one throwaway inference on a short silent buffer at app startup (or right after model load) so the user's first real dictation is fast.

### 4. Canary-1b-v2 specifics via transcribe.cpp

- **GGUF source:** `handy-computer/canary-1b-v2-gguf` on Hugging Face — 978M params (32-layer FastConformer encoder + 8-layer Transformer decoder). Quants: `F16`, `Q8_0` (1.1 GB), `Q6_K`, `Q5_K_M`, `Q4_K_M`. Per transcribe.cpp's `docs/models/canary.md`: "canary-1b-v2 | 8 [decoder depth] | 978M | 1.1 GB | 1.91% [WER on LibriSpeech test-clean] | 25 European." The mel filterbank and Hann window are baked into the GGUF, so no runtime recomputation. (Handy ships its own ~692 MB quant of the same model, so smaller footprints than Q8_0 are practical.)
- **Language/task control (CLI form, mirrored in the C params):** `-l`/`-sl <source>` sets the source language, `-tl`/`--target-language <target>` the target, and `--task translate` switches from ASR to speech translation. German→English:
  ```
  transcribe-cli -m canary-1b-v2-Q8_0.gguf --task translate -sl de -tl en input.wav
  ```
  German ASR is `-sl de -tl de` (task transcribe). **Language hint is mandatory — no auto-detect.** Per Handy's docs: "Canary does not auto-detect language — always set it manually, otherwise it translates instead of transcribing. Canary 1B v2 (~692 MB) — 25 European languages, full translation, high accuracy."
- **Domain vocabulary / prompt biasing:** the upstream Canary model supports injecting context into the decoder prompt to bias predictions with in-domain terms; whether transcribe.cpp exposes this via its params should be confirmed against the header/model card (the v1 port notably does *not* expose word/segment timestamps).
- **Known limitations:** no real-time streaming, no VAD, no auto language detection, and a ~400 s ceiling. Per `docs/models/canary.md`: "Every variant accepts up to about 6.7 minutes (400 s) of 16 kHz mono audio per call — the encoder's positional table is the binding limit… Longer audio is rejected up front with `TRANSCRIBE_ERR_INPUT_TOO_LONG` rather than silently truncated." Timestamps are not exposed in the current port. None of these matter for 5–30 s push-to-talk clips.
- **Warm session across invocations:** load `Model` once at startup, create one long-lived `Session`, and call run per clip — do **not** reload the 1.1 GB model each time. This is the single most important latency optimization.
- **Concurrency/thread safety:** the native context is **not** thread-safe, and there is a 0.x limitation that at most one compute call may be in flight across all sessions of a model. For a single-user dictation app this is fine: run transcription on one background thread (`Task.Run`) and serialize calls. Never overlap runs on the same session/model.
- **Smaller/faster option:** `canary-180m-flash` (182M params, 208 MB at Q8_0, 1.93% WER) — per the docs, "the ultralight variant; same 4-language coverage [en/de/es/fr] as the flash 1B" — is a good "fast/small" fallback that still covers German↔English.

### 5. Windows audio capture in .NET

- **NAudio** is the pragmatic default. `WasapiCapture` can request 16 kHz mono directly in shared mode (WASAPI does the sample-rate conversion), or use `WaveInEvent` for simplicity and resample with NAudio's managed `WdlResampler` / `MediaFoundationResampler` to 16 kHz mono. Convert 16-bit PCM to float32 by dividing by 32768, or capture IEEE float directly. `CSCore` is a capable alternative but less commonly maintained; raw WASAPI via `Windows.Media.Capture`/CsWin32 is more work for no real gain here.
- **Format ggml expects:** 16 kHz, mono, float32 in [-1, 1] — exactly what you pass to the native `run`.
- **Push-to-talk cleanly:** on hotkey-down, start `WasapiCapture` and accumulate `DataAvailable` buffers into a growing `List<float>`/`ArrayPool` buffer; on hotkey-up, stop, finalize the float array, and hand it to the transcription thread. Handle default-device changes via `MMDeviceEnumerator` notifications so a device swap mid-session doesn't crash capture.
- **VAD to trim silence and cut latency:** Silero VAD runs in .NET via ONNX Runtime. Usable packages: `ManySpeech.SileroVad` (1.1.x) and the `SileroVad` NuGet (both wrap `Microsoft.ML.OnnxRuntime`). This mirrors Handy's own pipeline: per the Handy project (MIT, 23,000+ GitHub stars), capture is "cpal at 16kHz → VAD filter (Silero via vad-rs) → transcription." Run VAD over the captured buffer to strip leading/trailing silence before sending to Canary — this both lowers latency and improves accuracy. `Microsoft.ML.OnnxRuntime` is at 1.30.x.

### 6. Global hotkey / push-to-talk

- **`RegisterHotKey` (user32) does NOT cleanly give key-up**, so it's a poor fit for hold-to-record/release-to-stop. It only signals a press.
- **Recommended: SharpHook** (wraps libuiohook; NuGet `SharpHook` 2.x). It gives true `KeyPressed`/`KeyReleased` events globally, which map directly to start/stop recording. Use `EventLoopGlobalHook` (or `TaskPoolGlobalHook`) so your handlers don't block the hook thread. Alternatively a raw `WH_KEYBOARD_LL` low-level hook via P/Invoke gives the same key-down/key-up semantics with zero dependencies but more code.
- **Background/tray app:** register the hook at startup and keep the app resident in the tray.
- **UAC/elevation:** a non-elevated hook/app cannot see or inject into windows running at higher integrity (elevated apps, some system dialogs). Document this; optionally offer an elevated mode. This is a Windows UIPI constraint, not a library limitation.

### 7. Pasting text at the cursor

- **Two-layer strategy (mirror what robust dictation tools do):**
  1. **Primary: clipboard + Ctrl+V.** Save the user's current clipboard, set the transcript, synthesize Ctrl+V via SendInput, then restore the previous clipboard after a short delay. This works across Office, browsers, Electron apps, JetBrains IDEs and terminals, and handles German umlauts/Unicode natively. It has the widest target coverage.
  2. **Fallback: SendInput with `KEYEVENTF_UNICODE`** (VK_PACKET), which injects Unicode characters directly and handles umlauts correctly — but note it can misbehave in some terminals (Windows Terminal has a known VK_PACKET rendering issue) and is subject to UIPI (silently fails against elevated windows, with no error returned).
- **UI Automation `TextPattern`** is the most "correct" for accessible controls but has poor coverage across browsers/Electron/terminals and is also blocked across integrity levels — use it only as a specialized path, not the default.
- **Clipboard restore caveat:** restoring clipboard contents perfectly (all formats, images) is imperfect; restore text and accept limitations, or make clipboard-preservation an option. Handy notes that switching windows during the processing window can cause paste into the wrong place — mitigate by pasting into the window that had focus at record time.

### 8. UI and app shell (2026)

For a tray-resident background utility on Windows-only:
- **WPF — recommended.** It is mature, fully supported on current .NET, has the best Visual Studio/Rider tooling, low memory footprint, fast enough startup for a tray app, and a huge ecosystem. Multiple 2026 framework surveys note WPF "is in better shape than it has been for years… open source, works with the latest version of .NET." Pair with **`H.NotifyIcon`** (modern tray-icon library with WPF support, context menus, and efficient background operation) for the tray + settings window + a small always-on-top overlay recording indicator.
- **WinUI 3 / Windows App SDK** has matured (SDK 1.8 stable, 2.0 in preview targeting .NET 10, with NativeAOT support in preview) and offers Fluent design, but it carries ecosystem uncertainty and heavier packaging; for a small background tool it's more risk than reward.
- **Avalonia** is excellent and cross-platform (used by JetBrains), a strong choice if you ever want Linux/macOS parity — but for Windows-only it adds a rendering stack you don't need.
- **Minimal system-tray-only** (WPF/WinForms + H.NotifyIcon, no main window) is arguably ideal here: the app is a hotkey daemon with a settings dialog and an overlay.

**Verdict:** WPF + H.NotifyIcon. Lowest-friction path for a senior .NET dev in Rider, minimal footprint, everything a dictation utility needs.

### 9. Packaging and distribution

- **Deployment:** framework-dependent keeps the download small but requires .NET on the machine; **self-contained** is friendlier for a consumer utility. Single-file publish works but **native assets (`transcribe.dll`, `ggml*.dll`, ONNX runtime) must be allowed to extract or be placed next to the exe** — set `IncludeNativeLibrariesForSelfExtract` or ship them as loose files in `runtimes/win-x64/native`.
- **NativeAOT:** feasible in principle (P/Invoke to a native DLL is AOT-friendly, especially with `LibraryImport`), but WPF is not AOT-ready, so AOT is only realistic for a headless/tray-minimal or WinUI variant. Don't make AOT a launch requirement.
- **Model handling:** the 1.1 GB Q8_0 model should be **downloaded on first run**, not bundled — bundling would make the installer enormous and duplicate what's on Hugging Face. Offer a smaller quant (`Q4_K_M`) or `canary-180m-flash` (208 MB) as a "fast/small" option. Verify the download with a checksum.
- **Installer/updater:** **Velopack** (1.2.x, successor to Squirrel/Clowd.Squirrel, MIT) is the current best-in-class for .NET: one-command packaging, fast delta updates, no-UAC updates in ~2 seconds, code-signing integration. It's a better fit than MSIX for a self-updating utility; Inno Setup or a plain zip are simpler but lack seamless auto-update.
- **Distribution channels:** the user has published to **WinGet** and **Chocolatey** before; both are straightforward here (Handy itself is on winget as `cjpais.Handy`). Velopack releases + winget/choco manifests is a clean pipeline. Code-sign the binaries to avoid SmartScreen friction.

### 10. Realistic assessment and alternatives

**Where the .NET path is genuinely harder than the Rust/Tauri original:**
- **Binding maintenance against a young C++ library.** transcribe.cpp is v0.2.x with a non-frozen ABI (v0.2.0 already broke device selection; 40 stars / 2 forks). The existing C# wrapper (`TranscribeCppSharp`) is early-stage (1 star) and pinned to an older upstream (0.1.3), so you'll likely be updating/regenerating interop yourself. The Rust path (`transcribe-cpp` crate, used by Handy) is better-tended.
- **Native build/CI complexity.** You'll need GitHub Actions/GitLab CI runners that build `transcribe.dll` + ggml with Vulkan on Windows (MSVC + Vulkan SDK), which is more moving parts than a pure managed build.
- **Cross-boundary debugging** (managed↔native crashes, marshalling bugs) is harder than staying in one language.
- **Churn:** keeping up with transcribe.cpp's rapid model/ABI additions is ongoing work.

**Alternatives, ranked for this use case:**

- **(a) Keep the Rust core, expose a C ABI, call from .NET.** Lowest-risk way to reuse proven code: compile your existing whisper-rs/transcribe-rs logic as a `cdylib` with a tiny C ABI and P/Invoke it. You keep the battle-tested inference path and only rewrite the UI/glue in .NET. Strong option if you want .NET UX without re-solving inference.
- **(b) Run transcribe.cpp as a sidecar process.** transcribe.cpp ships `transcribe-cli`; Handy ships a CLI-controllable app. You could spawn `transcribe-cli` per clip (simple, robust, process isolation protects your app from native crashes) or run a small long-lived local server. **There is no official transcribe.cpp HTTP/gRPC server binary**, so a persistent sidecar means wrapping the CLI or the Rust lib yourself and talking over stdin/stdout or a named pipe. Per-clip CLI invocation reloads the 1.1 GB model each time (slow) — so prefer a **persistent sidecar** that keeps the model warm and takes audio over a pipe. This cleanly sidesteps in-process interop and ABI-marshalling risk at the cost of IPC plumbing.
- **(c) ONNX Runtime + Canary ONNX export.** `istupakov/canary-1b-v2-onnx` exists and works with the Python `onnx-asr` package, which **does support German→English** (`target_language`). But `onnx-asr` implements the FastConformer feature extraction, tokenization and decoding in Python/NumPy; porting that pre/post-processing (mel filterbank, SentencePiece tokenization, AED decoding loop) to C# is **substantial, novel work**. `Microsoft.ML.OnnxRuntime` (1.30.x) with the **DirectML** execution provider would run well on the Intel iGPU without CUDA, and OpenVINO EP is another Intel-friendly option — but this path avoids one native binding only by forcing you to reimplement the ASR pipeline. Not recommended unless you specifically want to drop the C++ dependency.
- **(d) sherpa-onnx — the strongest low-risk fallback.** k2-fsa/sherpa-onnx has **official, prebuilt C# NuGet bindings** (`org.k2fsa.sherpa.onnx`, 1.13.x, Apache-2.0, with a `runtime.win-x64` native package — no self-build needed) and **added a C# API for NeMo Canary models** (plus Parakeet). Its `c-api.h` lists a dedicated `canary` offline model family alongside whisper, nemo_ctc, parakeet, sense_voice, etc. It runs entirely offline via ONNX Runtime, supports Canary's translation task, and is far more mature and better-maintained than the transcribe.cpp C# wrapper. GPU acceleration on Intel would go through DirectML. **If transcribe.cpp binding maintenance becomes painful, sherpa-onnx is the pragmatic production path for Canary-in-.NET today.**

**Overall verdict.** Building this in .NET is practical, and most of the app (capture, hotkey, paste, tray, updater) is genuinely easier in .NET than in Tauri. The one area of added risk is the native ASR binding. Recommendation: **prototype on TranscribeCppSharp** (fastest route to a working Canary-1b-v2 + Vulkan POC in C#), but architect the transcription behind an interface so you can swap in **sherpa-onnx** (lower maintenance, official bindings) or a **persistent Rust/CLI sidecar** if transcribe.cpp churn or Vulkan-on-Xe performance disappoint.

## Recommended Architecture (component description)

```
[Tray App shell: WPF + H.NotifyIcon]
   │  settings window, recording overlay, model download/mgmt
   │
   ├─ [Hotkey service: SharpHook]  key-down → StartCapture, key-up → StopCapture
   │
   ├─ [Audio service: NAudio WasapiCapture]  → 16 kHz mono float32 buffer
   │        │
   │        └─ [VAD: Silero via ONNX Runtime]  trim silence
   │
   ├─ [Transcription service]  (interface: ITranscriber)
   │        ├─ impl A: TranscribeCppSharp  → transcribe.dll + ggml*.dll (CPU+Vulkan)
   │        │             warm Model + Session, task=translate, sl=de, tl=en
   │        ├─ impl B: sherpa-onnx (org.k2fsa.sherpa.onnx) Canary  [fallback]
   │        └─ impl C: persistent sidecar over named pipe          [fallback]
   │
   └─ [Text injection]  clipboard+Ctrl+V (primary) / SendInput Unicode (fallback)
                         restore prior clipboard
[Packaging: Velopack self-contained; model downloaded first-run → %LOCALAPPDATA%]
[Distribution: WinGet + Chocolatey manifests; code-signed]
```

## Recommendations (staged)

**Phase 0 — Spike (1–2 days).** Add `TranscribeCppSharp` + `TranscribeCppSharp.Native.win-x64` to a console app. Download `canary-1b-v2-Q8_0.gguf`. Call `Backends.InitDefault()`, load the model with the Vulkan backend, run a 10-second German WAV with task=translate, sl=de, tl=en. **Benchmark latency on the E14's Xe iGPU for both Vulkan and CPU backends.** *Threshold to proceed: end-to-end transcription of a 10 s clip in well under ~3 s warm.* If Vulkan on Xe is not faster or is unstable, plan to ship the CPU (tinyBLAS) backend and/or the smaller `canary-180m-flash`.

**Phase 1 — Core loop (1 week).** WPF tray app + SharpHook hold-to-talk + NAudio capture → warm session → clipboard paste. Get the full "hold, speak, release, text appears" loop working end-to-end.

**Phase 2 — Quality/latency (3–5 days).** Add Silero VAD, first-run shader warmup, model-download UX with checksum, settings (language pair, model/quant selection, paste method), and the recording overlay.

**Phase 3 — Hardening (1 week).** Backend fallback (Vulkan→CPU), default-device-change handling, elevated-window paste caveats, robust error surfacing from native status codes, telemetry-free logging. Abstract transcription behind `ITranscriber` and stub a **sherpa-onnx** implementation as insurance.

**Phase 4 — Ship (3–5 days).** Velopack packaging, code signing, WinGet + Chocolatey manifests, CI that builds/tests and publishes native assets.

**Benchmarks that change the plan:** if warm Vulkan latency on Xe is disappointing → switch default to CPU backend or 180m-flash. If transcribe.cpp releases keep breaking the binding → cut over to sherpa-onnx. If native crashes prove hard to stabilize in-process → move to the persistent sidecar model.

**Realistic effort:** roughly **3–5 weeks** for a solid v1 for an experienced .NET dev, dominated by the native-binding/packaging/CI work rather than the C# app logic. Reusing TranscribeCppSharp saves the ~1–2 weeks a from-scratch binding would cost.

## Caveats
- I could not retrieve `include/transcribe.h` verbatim; exact struct field names, the precise representation of the translate task/source-lang/target-lang params, callback signatures, and the full status enum must be confirmed against the header (or the generated `TranscribeCppSharp.Interop`) before finalizing P/Invoke. Confirmed: blocking C API, `transcribe_init_backends`, `transcribe_model_free`, string-based `lang`, translation support, UTF-8 strings, 0.x non-frozen ABI, concurrency limit.
- transcribe.cpp is v0.2.x and moving fast (40 stars, 2 forks); TranscribeCppSharp is early-stage (pinned to 0.1.3, 1 star). Version drift between wrapper and upstream is a live maintenance concern.
- Vulkan performance on the Intel Xe/Arc iGPU for a 978M encoder-decoder is **unverified** for this specific machine — must be benchmarked; CPU may be competitive for short clips.
- Canary-1b-v2 licensing is ambiguous across sources: transcribe.cpp's `canary.md` labels the v2 weights "Apache-2.0," while the Hugging Face model-card metadata lists `license: cc-by-4.0`. Verify the exact license on the page you download from before commercial distribution.
- Whether transcribe.cpp exposes Canary's decoder-prompt biasing (custom vocabulary) is unconfirmed.
- The "no separate Vulkan install needed" claim holds for machines with current Intel drivers; ship a CPU fallback for machines with broken/absent Vulkan loaders.