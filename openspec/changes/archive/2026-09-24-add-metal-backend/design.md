## Context

See proposal.md for the motivation. The decisions come from the section "Decided for later macOS changes" of the archived `add-macos-shell` design (2026-09-22) and from explore mode on 2026-09-24. D-numbers of earlier designs are written "shell D11".

**Current state:**
- `AddTranscription()` is already registered on macOS. `TranscribeCppEngineFactory.IsVulkanAvailable()` asks the native library for `BackendRequest.BackendVulkan`, which the macOS package doesn't have, so **Auto** falls back to the CPU on every Mac start and logs it as a Vulkan fallback.
- `TranscribeCppSharp.Native.osx-arm64` 0.2.3 ships `libggml-metal.dylib` with its shader library embedded (`GGML_METAL_EMBED_LIBRARY`), and its `contract.json` lists the backends `metal` and `cpu`. The pinned wrapper `TranscribeCppSharp` 0.2.0 has `BackendRequest.BackendMetal`. No package change is needed.
- "Vulkan" stands for "the GPU" in three layers: `BackendPreference.Vulkan` (the setting), `NativeBackend.Vulkan` (the engine), and `TranscribeCppTranscriber.BackendName`, which reports `"Vulkan"` or `"CPU"` as `ITranscriber.ActiveBackend`. The tray ("Ready (Vulkan)") and the settings window ("Ready on Vulkan") show that string as it is.
- `JsonSettingsStore` reads enums with `JsonStringEnumConverter` in camelCase. An unknown enum value throws `JsonException`, and the whole file is then renamed to `settings.json.corrupt` and every setting goes back to its default. `AppSettings.SchemaVersion` (1) is written but never read.
- `TranscribeCppBenchmarkTests` measure on `NativeBackend.Vulkan` and read `GGML_VK_FORCE_MAX_ALLOCATION_SIZE` and `GGML_VK_FORCE_MAX_BUFFER_SIZE`.

## Goals / Non-Goals

**Goals:**
- **Auto** on macOS runs on Metal, with the same fallback, out-of-memory return and reload rules as Vulkan on Windows, in one code path.
- A Windows user's `settings.json` keeps all its values across the rename.
- The Mac's defaults (backend, model, warm-up) are set by measurement before `add-macos-dictation`.

**Non-Goals:**
- Choosing between several GPUs or GPU APIs on one platform. Each platform has exactly one GPU backend.
- A general migration framework. Format 2 is one step. The pre-pass (D3) is the place later steps go.
- Making a downgrade safe. An older build still resets the settings when it reads `"gpu"` (Risks).

## Decisions

### D1: One neutral `Gpu` value; the platform names it

`BackendPreference` becomes `Auto | Gpu | Cpu`, and `NativeBackend` becomes `Gpu | Cpu`. Only two places know the API:
- `TranscribeCppEngineFactory` maps `NativeBackend.Gpu` to `BackendRequest.BackendVulkan` on Windows and `BackendRequest.BackendMetal` on macOS, with `#if WINDOWS`. `IsVulkanAvailable()` becomes `IsGpuAvailable()`, which checks the same request.
- The name shown to users and in the log, `"Vulkan"` on Windows and `"Metal"` on macOS, is one constant chosen with `#if WINDOWS` next to the factory's mapping. `BackendName` returns it for `Gpu`, so `ActiveBackend`, "Ready (Metal)", "Ready on Metal" and the log follow without further changes. The radio button binds its label to the same constant: "Metal (GPU) only" or "Vulkan (GPU) only". The `_V` access key stays: `_Vulkan (GPU) only` on Windows and `_Metal (GPU) only` on macOS.

The transcriber's Vulkan names (`VulkanWarmUpSeconds`, `_longestVulkanRun` and the like) become GPU names. The logic doesn't change.

*Rejected:* a value per API (`Vulkan | Metal | Cpu`). Every `== NativeBackend.Vulkan` would become "is it a GPU?", and the stored setting would differ per platform, although each platform has only one GPU backend.

### D2: The JSON value `gpu`

The setting is stored as `"gpu"` on both platforms. `settings.json` is per machine, so a value that means "this platform's GPU" is exact. The six `transcription` scenario names that contain "Vulkan" keep their names during the change, because OpenSpec can't rename a scenario through a delta: `validate` and `archive` reject a MODIFIED requirement that drops a scenario name the main spec still has. Their text says "the GPU backend". Just before archiving, the names are renamed in the main spec and in the delta together (task 5.4).

### D3: Format 2 through a `JsonNode` pre-pass

`JsonSettingsStore.Read()` parses the file into a `JsonNode`, migrates it, and then deserializes the node:

```
 file --> JsonNode.Parse
            |
            +-- schemaVersion missing or < 2:
            |      transcription.backend == "vulkan"  -->  "gpu"
            |      schemaVersion = 2
            v
          node.Deserialize<AppSettings>   (a JsonException still leads to .corrupt)
```

- The comparison is exact and case-sensitive, as `JsonStringEnumConverter` reads; any other value is left to the deserializer.
- A missing `transcription` section, or a missing `backend`, is left alone and takes its default, as "Partial settings files" says.
- `AppSettings.SchemaVersion` becomes 2, so a new file and every save write version 2.
- A file with a version higher than 2, from a newer build, isn't migrated and is read as it is.

*Rejected:* a converter that also accepts `"vulkan"`. It works, but it stays in the code forever, it's specific to one enum, and it leaves `schemaVersion` unused.

### D4: Migrated in memory, written on the next save

`Load()` doesn't write the file. `Current` holds the migrated settings, and the next `SaveAsync` writes them with version 2. The migration runs on every start until then, which costs one tree walk of a small file. The app does no file write at startup, which could fail on its own, and a user who never saves keeps a file that older builds still read.

### D5: The out-of-memory return stays for Metal

The return to the GPU after an out-of-memory error stays as it is, renamed. On unified memory, Metal allocates from the same RAM as the CPU, so an out-of-memory error may never happen, or may happen at the same length on both backends. The long-clip benchmark (D6) records which, and the result is written below. The logic isn't removed on macOS even if Metal never runs out, because it costs nothing when the error doesn't come.

### D6: The benchmark checkpoint, the last tasks of this change

`TranscribeCppBenchmarkTests` become backend-neutral (`NativeBackend.Gpu`, `IsGpuAvailable`). The Vulkan environment variables are reported only on Windows. On the development Mac (MacBook Air M4, 16 GB), with the German clip from `HardwareTestAssets`, run through the test executable with `-explicit only -class "*.TranscribeCppBenchmarkTests" -diagnostics`:

| Run | Question | Decides |
|---|---|---|
| Latency table, all installed catalog models, GPU and CPU | Warm 10 s clip well under 3 s? Metal faster than the CPU? | macOS default backend |
| Q8_0 against Q4_K_M, same clip | Does Q4_K_M match Q8_0 in accuracy? | macOS default model |
| Cancelled run on the CPU | Time to return after a cancel | nothing, recorded |
| Long clips on Metal, 30 s to 399 s | Does Metal run out of memory on unified memory? | whether D5 matters on the Mac |
| First run after a 1 s warm-up on Metal, against 10 s | Is 10 s of noise needed to compile Metal's pipelines? | warm-up length per platform |

The last row needs a small addition to the benchmark: it loads the model, warms up with the given input, and times the first real run.

**Rules for the results:**
- If Metal is slower than the CPU or unstable, the macOS default backend becomes `cpu` through a per-platform default of `TranscriptionSettings.Backend`. Windows keeps `auto`.
- If Q4_K_M matches Q8_0 in accuracy and is faster, the macOS default model becomes Q4_K_M through a per-platform `ModelCatalog.DefaultModelId`. That touches `model-management` and needs a delta there.
- If a 1 s warm-up gives the same first run on Metal, the GPU warm-up input becomes per platform, and "Warm-up before ready" says so.
- Each change of default updates this change's spec deltas and tasks before the PR.

**Results:** *(filled in when the checkpoint has run)*

## Risks / Trade-offs

- [A downgrade to an older build reads `"gpu"` as an unknown value and resets every setting] → Only a manual downgrade can do that, because the MSI refuses older versions and the macOS installer will too. The file isn't rewritten until the user saves (D4), so a user who never opens the settings keeps an older-readable file.
- [Metal is slower than the CPU for this model, or unstable] → The benchmark finds it before dictation, and the macOS default changes (D6).
- [Metal pipelines compile at first use, making the first dictation slow] → The GPU warm-up covers it. The benchmark measures whether its 10 s input is needed.
- [The embedded Metal library fails to load on a macOS version the package wasn't built for] → The fallback to the CPU on **Auto** covers it, and the log names the reason. The minimum stays macOS 14 (shell D1).
- [The rename touches many test lines, which could hide a change in logic] → The rename is its own task, done before any Metal code, with the full test suite green on Windows and macOS after it.

## Migration Plan

1. The rename to `Gpu`, with Windows behaving exactly as today and all tests green on both platforms.
2. The settings migration with its tests.
3. The Metal mapping and the per-platform names.
4. The benchmark on the M4, the results in this design, and any change of default.
5. Check by hand: a Windows `settings.json` with `"backend": "vulkan"` keeps its other values and shows **Vulkan (GPU) only**; the Mac shows "Ready on Metal".

**Rollback:** revert the pull request. A file saved as version 2 with `"gpu"` is then reset by the older build (Risks).
