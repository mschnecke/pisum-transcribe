## Context

This change builds on `scaffold-app-shell`: Generic Host, `AppPaths` (`models\` folder), `ISettingsStore`, `ShutdownCoordinator`, and `TrayIconService.AddMenuItem`. `AddMenuItem` can only add items; it cannot hide them. H.NotifyIcon raises `PreviewTrayContextMenuOpen` before the tray menu shows. transcribe.cpp loads Canary models from single GGUF files, and the mel filterbank and tokenizer are embedded in the file. The Hugging Face repositories `handy-computer/canary-1b-v2-gguf` and `handy-computer/canary-180m-flash-gguf` publish the quantizations with LFS SHA-256 hashes. Both list `license: cc-by-4.0`, and so do NVIDIA's original model cards `nvidia/canary-1b-v2` and `nvidia/canary-180m-flash`. Note that `docs/idea.md` flags a mismatch: transcribe.cpp's docs say Apache-2.0.

## Goals / Non-Goals

**Goals:**
- Reproducible, verified model installs: a pinned revision plus a SHA-256 hash.
- A first run that gets a non-technical user from zero to an installed model in one window.
- A model store API that `add-transcription-engine` and `add-settings-window` use without changes.

**Non-Goals:**
- Resuming interrupted downloads. A failed download restarts from zero. Revisit if users on slow links complain.
- Custom or user-supplied model files, or models outside the catalog.
- Deleting models from the UI (see `add-settings-window`).
- Selecting an already installed model without downloading it (see `add-settings-window` and the risks below).
- Downloading via a proxy that needs special configuration beyond the system proxy, which `HttpClient` uses by default.

## Decisions

### D1: Static, pinned catalog in code

`ModelCatalog` is a static list of `SpeechModel` records. Download URLs use the immutable revision form `https://huggingface.co/<repo>/resolve/<commit>/<file>`, so the hash can never drift from the file:

| Id | Repo @ revision | File | Bytes | SHA-256 |
|---|---|---|---|---|
| `canary-1b-v2-q8_0` | `handy-computer/canary-1b-v2-gguf` @ `e2d8e6d7f2accc1259dc5497b517b4083047e44b` | `canary-1b-v2-Q8_0.gguf` | 1144290016 | `224f83d1bc487b3303b495a7d6874912fdece93de19d1a04b550829c30a5d289` |
| `canary-1b-v2-q4_k_m` | same | `canary-1b-v2-Q4_K_M.gguf` | 735476448 | `49e0a67e219bec95a254c2348460b6350e75a7ac6f93a131e48244b4c7cb53b9` |
| `canary-180m-flash-q8_0` | `handy-computer/canary-180m-flash-gguf` @ `456e6049062ecf06f1a0f4607f2ee3dc80ebbf8a` | `canary-180m-flash-Q8_0.gguf` | 218447552 | `e13c7f5d0952b056a027cfffec13e3a3a134d1608babed24f983568f141e297c` |

The revisions, sizes and hashes were checked against the Hugging Face API on 2026-09-17. Both pinned revisions were the repositories' current `main` at that time.

`SpeechModel` also carries `IReadOnlyList<string> Languages` (ISO 639-1 codes) and the attribution that CC BY 4.0 asks for:

| Field | Example (`canary-1b-v2-q8_0`) |
|---|---|
| `Creator` | NVIDIA |
| `LicenseName` | CC BY 4.0 |
| `LicenseUrl` | `https://creativecommons.org/licenses/by/4.0/` |
| `Changes` | Converted from `nvidia/canary-1b-v2` to GGUF and quantized to Q8_0 by handy-computer |
| `SourceRepository` | `handy-computer/canary-1b-v2-gguf` |

Translation is always X→en or en→X, so no extra matrix is needed. `add-transcription-engine` validates language pairs against this record.

*Why Q8_0 as the default:* it is the size `docs/idea.md` benchmarks against, and its accuracy is closest to F16. Q4_K_M is the "smaller" option, and 180M Flash is the "fast" option for weak machines. The larger F16 and F32 files are left out on purpose.

*Alternative:* fetching the catalog at runtime from a JSON manifest. This was rejected because it adds a hosted file and a trust question for three entries.

### D2: `IModelStore` API

```csharp
internal interface IModelStore
{
    string GetModelPath(SpeechModel model);
    bool IsInstalled(SpeechModel model);
    Task InstallAsync(SpeechModel model, IProgress<DownloadProgress> progress, CancellationToken ct);
    event EventHandler<SpeechModel>? ModelInstalled;
}
```

`InstallAsync` runs these steps:

1. Check disk space with `DriveInfo` for the models folder: at least `SizeBytes + 100 MB` free.
2. Send the request with `HttpCompletionOption.ResponseHeadersRead`. Hugging Face answers the `resolve` URL with a 302 redirect to its CDN under `hf.co`. `HttpClient` follows it, and it refuses a redirect from HTTPS to HTTP, so the download stays on HTTPS. If the response has a `Content-Length` that differs from `SizeBytes`, throw `ModelIntegrityException` before anything is written.
3. Stream the response into `<file>.partial` with an 81,920-byte buffer. The SHA-256 is computed incrementally with `IncrementalHash` while writing, so there is no second 1 GB read pass. As soon as more than `SizeBytes` arrive, throw `ModelIntegrityException`, so a wrong response cannot fill the disk.
4. Report progress at most every 250 ms. The total is always `SizeBytes`, because `Content-Length` can be missing.
5. Compare the hash. On a match, `File.Move(partial, final, overwrite: true)` and raise `ModelInstalled`. On a mismatch, delete the partial file and throw `ModelIntegrityException`.
6. On any exception or cancellation, delete the `.partial` file in a `finally` block.

Leftover `*.partial` files from a crash are deleted at startup.

`HttpClient` comes from `IHttpClientFactory` (`services.AddHttpClient`), with `Timeout = InfiniteTimeSpan`. A per-read inactivity timeout (60 s through a linked `CancellationTokenSource`) turns a stalled connection into a failure.

`ModelStore` links the caller's token with `IHostApplicationLifetime.ApplicationStopping`, so every caller's download is cancelled when the application exits. Without this, `ShutdownCoordinator` would stop and dispose the host, which disposes the `HttpClient` handlers, and a running download would fail with `ObjectDisposedException` instead of being cancelled. If the 4.5 s watchdog ends the process before the `finally` block runs, the startup cleanup removes the `.partial` file.

`ModelInstalled` is raised on the thread that finished the download. Subscribers that touch WPF or the tray marshal to the dispatcher.

### D3: Installed means "exists with the catalog size"

Hashing 1.1 GB takes several seconds on every start, which is too expensive. Size equality catches truncation. Bit-level corruption after install is rare, and it surfaces as a model-load failure in `add-transcription-engine`, which offers a re-download.

### D4: First-run setup window (WPF + CommunityToolkit.Mvvm)

`ModelSetupWindow` with `ModelSetupViewModel` (`ObservableObject`, `[RelayCommand]`). It has a model list (radio list with name, size, languages), a license and attribution panel (D1 fields, with the license name as a link), **Download** / **Cancel** / **Close** buttons, a progress bar with "x of y MB", and an error area with **Retry**. Sizes use binary units labeled MB and GB, as Windows Explorer does: 1.07 GB, 701 MB and 208 MB.

`ModelSetupHostedService` shows the window at startup when `!IsInstalled(selected)`. It shows the window without blocking (`Show`, not `ShowDialog`) on the dispatcher, so host startup continues. There is one window instance at a time; the tray item activates it when it is already open.

**Selection is saved when the download starts.** `DownloadCommand` saves `model.selectedModelId` as the chosen model through `ISettingsStore.SaveAsync`, and only then calls `InstallAsync`. On success, the view model requests close.

*Why before the download:* `ModelInstalled` is raised inside `InstallAsync`, and `add-transcription-engine` loads an installed model only when it is the selected one. If the selection were saved after `InstallAsync` returns, the engine would still see the old selection, and the chosen model would not load until the next start. A cancelled or failed download leaves the chosen model selected but not installed. That is the state the window opened in, and the window preselects the chosen model next time.

*Alternatives:*
- Save after success and add a settings-changed event that the engine and tray also react to. This brings `ISettingsStore.Changed` forward from `add-settings-window`, and its `SettingsApplier` would later have to take over these listeners.
- Let the store save the selection inside `InstallAsync`, before raising the event. This couples the store to settings, and `add-settings-window` downloads models without selecting them.

**Closing during a download asks first.** The window's `Closing` handler asks the view model whether it may close:
- No download running: close.
- Download running and `ApplicationStopping` has fired: close without asking. The store has already cancelled the download (D2), and WPF ignores `Cancel` during `Application.Shutdown` anyway.
- Download running otherwise: ask "Closing cancels the download. Close anyway?" (Yes/No). Yes cancels the download and closes. No sets `e.Cancel = true`, and the download continues.

The view model gets the confirmation as a callback, so tests do not show a dialog. The window closes at once after Yes. The download task finishes its cleanup in the background, and its cancellation is not reported as an error.

*Why CommunityToolkit.Mvvm 8.4.2:* its source-generated MVVM keeps view models small and testable. `add-settings-window` reuses it.

### D5: Settings section

This change adds `ModelSettings(string SelectedModelId = "canary-1b-v2-q8_0")` to `AppSettings`. `ModelCatalog.Resolve(id)` returns the default for unknown IDs. Following the `AppSettings` rules, `JsonSettingsStore` replaces a `"model": null` section with the default after loading.

### D6: Tray item visibility is checked when the menu opens

`ITrayIconService.AddMenuItem(string header, Action onClick, Func<bool>? isVisible = null)`. `TrayIconService` handles H.NotifyIcon's `PreviewTrayContextMenuOpen` and sets each item's visibility from its callback before the menu shows. An item without a callback is always visible. The separator above **Exit** is hidden when no item above it is visible.

`ModelSetupHostedService` adds **Download model…** with `isVisible: () => !store.IsInstalled(ModelCatalog.Resolve(settings.Current.Model.SelectedModelId))`.

*Why check when the menu opens:* several things change whether the item should show: a finished install, the damaged-file cleanup in `add-transcription-engine` (D7 there), a file deleted in Explorer, and model deletion in `add-settings-window`. A check when the menu opens covers all of them without events or ordering rules. It is a file-exists and file-length read, which is cheap enough for the UI thread.

*Alternative:* `AddMenuItem` returns a handle with `IsVisible`, and the store raises events on install and removal. Every deletion would then have to go through the store, and a file deleted outside the app would go unnoticed until the next start.

## Risks / Trade-offs

- [The Hugging Face repositories could be deleted or made private.] → URLs are pinned by revision and hashes are verified. If a repository disappears, a catalog update in a new app version is required. The error message names the source so users can report it.
- [License ambiguity between CC-BY-4.0 on Hugging Face and Apache-2.0 in transcribe.cpp's docs.] → NVIDIA's original model cards also list CC-BY-4.0, and a GGUF conversion cannot relicense the weights, so the Apache-2.0 label is most likely wrong. Both licenses allow commercial use, and the app does not redistribute the weights, because users download them from Hugging Face. The window shows the attribution that CC BY 4.0 asks for (D1). Legal confirmation before public distribution is tracked for the packaging change.
- [Corporate proxies, TLS inspection or firewall allowlists can break downloads.] → `HttpClient` uses the system proxy. Allowlists need `huggingface.co` and the CDN under `hf.co`. The error message includes the HTTP status or exception type, and the details go to the log.
- [A proxy or captive portal returns a different file than the catalog file.] → The size checks in D2 fail before or while writing, and the hash check catches the rest.
- [A 1.1 GB download with no resume is painful on flaky links.] → The smaller models are offered in the same window, and closing the window during a download asks for confirmation first. Resume is a deferred non-goal.
- [The selected model is missing while another catalog model is installed, for example after a manual delete or the damaged-file cleanup in `add-transcription-engine`.] → The setup window only offers downloads. Downloading the installed model again works, because the move overwrites the file and raises `ModelInstalled`, but it costs the full download. `add-settings-window` lets the user select any installed model and reloads the engine.
- [Q8_0 might be needlessly large if Q4_K_M proves equally accurate for German→English.] → The benchmark task in `add-transcription-engine` compares both. Changing the default would be a follow-up change.
