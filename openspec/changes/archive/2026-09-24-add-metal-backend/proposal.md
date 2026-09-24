## Why

On macOS the engine asks for Vulkan, finds none and runs on the CPU, although the native package already ships Metal. Dictation on the Mac (`add-macos-dictation`, #20) needs the GPU for a warm 10 s clip well under 3 s, and its defaults must be settled by a benchmark before it starts. The setting value `vulkan` also doesn't fit a platform where the GPU backend is Metal.

Tracked in issue #18. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design, and from explore mode on 2026-09-24.

## What Changes

- **Metal on macOS.** The GPU backend is Metal on macOS and stays Vulkan on Windows. Selection, the CPU fallback, the warm-up, the out-of-memory return and the reload rules are the same on both platforms; only the name the user sees differs ("Ready (Metal)", "Metal (GPU) only").
- **The setting value `vulkan` becomes `gpu`.** **BREAKING** for `settings.json`: the value is renamed on both platforms.
  - The settings file gets schema version 2. A file of version 1 (or without a version) is migrated when it is read: `vulkan` becomes `gpu`. Without the migration the file would count as corrupt, and every setting would go back to its default.
  - The migration stays in memory and is written with the next save. The app doesn't write the file at startup.
- **The benchmark checkpoint on the development Mac** (MacBook Air M4, 16 GB), as the last tasks of this change:
  - Metal against the CPU for each installed catalog model, with the target of a warm 10 s clip well under 3 s
  - Q8_0 against Q4_K_M in accuracy
  - the cancelled run on the CPU
  - long clips up to 399 s on Metal, which show whether the out-of-memory return matters on unified memory
  - the warm-up length on Metal
  - The results are recorded in `design.md`. If Metal is slower than the CPU or unstable, or Q4_K_M matches Q8_0, the macOS default backend or model changes in this change, before the PR.
- **Not included:** dictation on macOS (`add-macos-dictation`, #20). The Mac's tray shows the engine status as today, now with Metal.
- **The native packages don't change.** `TranscribeCppSharp.Native.osx-arm64` 0.2.3 already contains the Metal backend with its shader library embedded.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `transcription`:
  - "Transcription settings": the backend values are `auto`, `gpu` and `cpu`.
  - "Warm-up before ready", "Model stays loaded", "Backend selection and fallback", "Failures during transcription", "Clean shutdown during transcription", "Request cancelled by the caller", "Reload on model or backend change": the GPU backend, Vulkan on Windows and Metal on macOS, in place of Vulkan.
- `dictation`: "Cancel a transcription" and "Tray icon states": the GPU backend, with "Ready (Vulkan)" on Windows and "Ready (Metal)" on macOS.
- `settings-window`: "Backend settings" and "Apply changes without restart": the GPU option named for the platform.
- `settings-storage`: a new requirement, "Settings format migration": a file of an older format is migrated when it is read, and never counts as corrupt for it.

## Impact

- **Depends on:** `add-macos-shell` (#15), which is merged. It runs in parallel with the other macOS changes.
- **Code:**
  - `Transcription/`: `BackendPreference.Vulkan` and `NativeBackend.Vulkan` become `Gpu`. `TranscribeCppEngineFactory` maps the GPU to Vulkan on Windows and Metal on macOS, and `IsVulkanAvailable` becomes `IsGpuAvailable`. The transcriber's Vulkan names become GPU names, and the backend name shown to the user comes from the platform.
  - `Settings/`: `JsonSettingsStore` migrates the file before it deserializes it, and `AppSettings.SchemaVersion` becomes 2.
  - `SettingsWindow/`: the radio button and its view model property.
- **Settings:** a Windows user's `"backend": "vulkan"` keeps working. An older build that reads a saved `"gpu"` resets its settings; only a manual downgrade can do that, because the MSI refuses older versions.
- **Tests:** the transcriber tests are renamed to the GPU. New tests cover the migration. `TranscribeCppBenchmarkTests` become backend-neutral, and their Vulkan-only environment variables stay Windows-only.
- **Docs:** `docs/idea.md`, `docs/roadmap.md` and `CLAUDE.md` where they name Vulkan as the only GPU backend.
