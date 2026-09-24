The order follows the dependencies. First the rename, with Windows behaving exactly as today, then the settings migration, then Metal, and the benchmark last. `dotnet test Pisum.Transcribe.slnx` must pass on the Mac and in CI on Windows after every group.

## 1. The rename to the GPU

- [x] 1.1 Rename `BackendPreference.Vulkan` and `NativeBackend.Vulkan` to `Gpu`, and `INativeSpeechEngineFactory.IsVulkanAvailable` to `IsGpuAvailable` (D1). Rename the Vulkan names in `TranscribeCppTranscriber` (`VulkanWarmUpSeconds`, the fields of the out-of-memory return, comments and XML docs) to GPU names without changing any logic. Verify: `dotnet build` passes for both frameworks.
- [x] 1.2 Rename the tests to match: `TranscribeCppTranscriberTests`, `TranscribeCppTranscriberLoggingTests`, `FakeNativeSpeechEngineFactory`, `ModelSectionViewModelTests`, `SettingsViewModelTests`, `SettingsDialogTests`, `DictationFeedbackTests`, `JsonSettingsStoreTests`. Test methods and assertions change names only. Verify: the full suite passes on the Mac, and the diff of the tests has no changed numbers, timings or expected outcomes.

## 2. The settings migration

- [x] 2.1 Add the `JsonNode` pre-pass to `JsonSettingsStore.Read()` and set `AppSettings.SchemaVersion` to 2 (D3, D4, spec `settings-storage` "Settings format migration"). Verify: `JsonSettingsStoreTests` cover a file of version 1 and one without a version with `"backend": "vulkan"`, which load as `Gpu` with every other value kept and no `.corrupt` file; the file's bytes unchanged after `Load()`; version 2 and `"gpu"` written by the next `SaveAsync`; a file without a `transcription` section or `backend`; an unknown backend value, still renamed to `.corrupt`; and a file of version 3 read without migration.

## 3. Metal on macOS

- [x] 3.1 Map `NativeBackend.Gpu` in `TranscribeCppEngineFactory` to `BackendRequest.BackendVulkan` on Windows and `BackendRequest.BackendMetal` on macOS, and add the GPU backend name constant, `"Vulkan"` or `"Metal"`, chosen with `#if WINDOWS` (D1). `TranscribeCppTranscriber.BackendName` returns it for `Gpu` (spec `transcription` "Backend selection and fallback"). Verify: a macOS `Hardware` test loads the installed default model on `NativeBackend.Gpu` and runs the warm-up input, skipped without the model; a unit test sees `ActiveBackend` equal to the constant on the GPU backend.
- [x] 3.2 Bind the settings window's GPU radio button label to the platform name: `_Vulkan (GPU) only` on Windows, `_Metal (GPU) only` on macOS (spec `settings-window` "Backend settings"). Verify: a headless `SettingsDialogTests` test finds the label with the constant's name, and `ModelSectionViewModelTests` show "Ready on" the constant.
- [ ] 3.3 Run the app on the Mac with the dev bundle and a downloaded model (spec `dictation` "Tray icon states", `settings-window` "Backend settings"). Verify: the log says the model is ready on Metal, the menu bar tooltip says "Ready (Metal)", the settings window shows "Ready on Metal" and **Metal (GPU) only**, and switching to **CPU only** and back reloads without a restart.

## 4. The benchmark checkpoint on the M4

- [x] 4.1 Make `TranscribeCppBenchmarkTests` backend-neutral (D6): `NativeBackend.Gpu`, `IsGpuAvailable`, the Vulkan environment variables reported only on Windows, and the macOS test executable named in the class doc. Add the warm-up comparison run: load the default model on the GPU, warm up on 1 s of silence or on the 10 s noise, and time the first real run of the German clip. Verify: the benchmark tests build on both frameworks and run on the Mac with `-explicit only -class "*.TranscribeCppBenchmarkTests" -diagnostics`.
- [ ] 4.2 Download every catalog model on the development Mac (MacBook Air M4, 16 GB) and run the benchmark: the latency table on Metal and the CPU, the cancelled run on the CPU, the long clips on Metal up to 399 s, and the warm-up comparison. Compare the accuracy of Q8_0 and Q4_K_M on the German clip. Verify: the results table is filled in under "Results" in `design.md`, with the date and the macOS version.
- [ ] 4.3 Apply the rules of D6 to the results. When a Mac default or the warm-up input changes: make it per platform in code, update the spec deltas (`transcription`, and `model-management` for a default model), and add its tests. When nothing changes, write that down in `design.md`. Verify: `openspec validate add-metal-backend --strict` passes, and the design names the Mac defaults.

## 5. Docs and wrap-up

- [ ] 5.1 Update the docs:
  - `docs/idea.md`: the GPU backend on each platform where it names Vulkan alone
  - `docs/roadmap.md`: the change done and the checkpoint's outcome
  - `CLAUDE.md`: the `Transcription/` line of the layout if it names the backend, and the settings format version under **Settings**

  Verify: the texts match the code.
- [ ] 5.2 Check the migration by hand on Windows, through CI or a Windows machine: a `settings.json` with `"backend": "vulkan"` and a custom hotkey starts with the hotkey kept and **Vulkan (GPU) only** chosen. Verify: noted in the PR.
- [ ] 5.3 Run `openspec validate add-metal-backend --strict`, and `dotnet build` plus `dotnet test Pisum.Transcribe.slnx` on the Mac and in CI on Windows. Verify: all pass. The PR references #18 without a closing keyword.
- [x] 5.4 Just before archiving, in the same PR: rename the six `transcription` scenarios that still name Vulkan, both in `openspec/specs/transcription/spec.md` and in this change's `specs/transcription/spec.md`. OpenSpec can't rename a scenario through a delta, and nothing else references these names.
  - "Warm-up input on Vulkan" → "Warm-up input on the GPU"
  - "Auto with working Vulkan" → "Auto with a working GPU backend"
  - "Auto with broken Vulkan" → "Auto with a broken GPU backend"
  - "Forced Vulkan fails" → "Forced GPU backend fails"
  - "Vulkan fails during the return" → "The GPU backend fails during the return"
  - "GPU error with forced Vulkan" → "GPU error with forced GPU backend"

  Verify: `openspec validate add-metal-backend --strict` passes, and `grep -n "Scenario:.*Vulkan"` finds none of the six in either file.
