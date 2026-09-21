## Purpose

Defines how Pisum Transcribe turns recorded 16 kHz mono audio into text, or into translated text, with a locally loaded speech model that is kept warm and uses the GPU when possible.

## ADDED Requirements

### Requirement: Transcription settings
The application SHALL persist these transcription settings with the given defaults:
- `transcription.task`: `translate` or `transcribe`, default `translate`
- `transcription.sourceLanguage`: ISO 639-1 code, default `de`
- `transcription.targetLanguage`: ISO 639-1 code, default `en`
- `transcription.backend`: `auto`, `vulkan` or `cpu`, default `auto`

#### Scenario: Defaults on first start
- **WHEN** no transcription settings have been saved
- **THEN** the task is `translate`, the source language is `de`, the target language is `en` and the backend is `auto`

### Requirement: Language validation against the model
A transcription request SHALL be rejected with a "language not supported" error before any audio is processed when:
- the source language is not supported by the selected model, or
- the task is `translate` and the target language is not supported by the model, the two languages are equal, or neither of them is `en`.

For the `transcribe` task, the output language SHALL be the source language, and the target language setting SHALL be ignored.

#### Scenario: German to English translation
- **WHEN** the selected model is `canary-1b-v2-q8_0`, the task is `translate`, the source is `de` and the target is `en`
- **THEN** the request is accepted

#### Scenario: Unsupported language for the small model
- **WHEN** the selected model is `canary-180m-flash-q8_0` and the source language is `pl`
- **THEN** the request is rejected with a "language not supported" error

#### Scenario: Translation between two non-English languages
- **WHEN** the task is `translate`, the source is `de` and the target is `fr`
- **THEN** the request is rejected with a "language not supported" error

#### Scenario: Transcription ignores the target language
- **WHEN** the task is `transcribe`, the source is `de` and the target is `en`
- **THEN** the request is accepted
- **AND** the result text is German, not translated into English

### Requirement: Background model loading
When the selected model is installed at startup, or becomes installed while the application runs, the application SHALL load it in the background without blocking the tray menu. The engine SHALL report the status `Loading` while loading, `Ready` when usable, and `Failed` when loading fails.

#### Scenario: Startup with installed model
- **WHEN** the application starts and the selected model is installed
- **THEN** the engine status becomes `Loading`
- **AND** the tray menu stays responsive while the model loads
- **AND** the status becomes `Ready` when loading and warm-up finish

#### Scenario: Model installed during the session
- **WHEN** the selected model becomes installed while the application runs
- **THEN** the engine starts loading that model without an application restart

#### Scenario: Model downloaded again after a failure
- **WHEN** loading failed because the model file was damaged and deleted, and the user downloads the model again
- **THEN** the engine loads the model without an application restart

### Requirement: Warm-up before ready
After loading a model, the engine SHALL run one warm-up inference before reporting `Ready` and SHALL discard its output. On the Vulkan backend, the input SHALL be 10 seconds of low-level noise, so the first real transcription does not pay the one-time GPU pipeline compilation cost. On the CPU backend, which compiles no GPU pipelines, the input SHALL be 1 second of silence.

#### Scenario: First real transcription after warm-up
- **WHEN** the engine reports `Ready` and the first real request arrives
- **THEN** the request runs without triggering a model load or warm-up

#### Scenario: Warm-up input on Vulkan
- **WHEN** the model loads on the Vulkan backend
- **THEN** the warm-up runs on 10 seconds of low-level noise

#### Scenario: Warm-up input on CPU
- **WHEN** the model loads on the CPU backend, including the reload after a Vulkan failure
- **THEN** the warm-up runs on 1 second of silence

#### Scenario: Warm-up output truncated
- **WHEN** the warm-up inference stops at the model's output limit
- **THEN** the warm-up counts as successful
- **AND** the status becomes `Ready`

### Requirement: Model stays loaded
The loaded model SHALL be reused for all transcription requests until the application exits, the model or backend selection changes, or a backend error forces a reload on the CPU backend. It SHALL NOT be reloaded per request.

#### Scenario: Consecutive transcriptions
- **WHEN** two transcription requests complete one after another
- **THEN** the model was loaded exactly once

### Requirement: Backend selection and fallback
With backend `auto`, the engine SHALL use the Vulkan GPU backend when it is available and loads and warms up successfully. Otherwise it SHALL use the CPU backend. With `vulkan`, a Vulkan failure SHALL set the status to `Failed`, with no fallback. With `cpu`, only the CPU backend SHALL be used. A model file rejected as invalid SHALL NOT count as a Vulkan failure. The backend in use SHALL be reported with the `Ready` status and written to the log.

#### Scenario: Auto with working Vulkan
- **WHEN** the backend is `auto` and Vulkan loads and warms up successfully
- **THEN** the status is `Ready` with backend `Vulkan`

#### Scenario: Auto with broken Vulkan
- **WHEN** the backend is `auto` and Vulkan is unavailable or fails during loading or warm-up
- **THEN** the engine loads the model on the CPU backend
- **AND** the status is `Ready` with backend `CPU`
- **AND** the reason for the fallback is logged

#### Scenario: Forced Vulkan fails
- **WHEN** the backend is `vulkan` and Vulkan fails to load
- **THEN** the status is `Failed`
- **AND** no CPU fallback is attempted

#### Scenario: Invalid model file with auto backend
- **WHEN** the backend is `auto` and the model file is rejected as invalid while loading on Vulkan
- **THEN** no CPU load is attempted
- **AND** the load failure handling for invalid model files applies

### Requirement: Serialized transcription
The engine SHALL process at most one transcription at a time, including warm-up, and SHALL process queued requests in arrival order.

#### Scenario: Overlapping requests
- **WHEN** a second request arrives while a first request is running
- **THEN** the second request starts only after the first finished
- **AND** results are returned to the matching callers

### Requirement: Audio input contract
Transcription input SHALL be 16 kHz mono 32-bit float samples in the range [-1, 1]. An empty input SHALL return an empty text result without invoking the model. An input longer than the loaded model's maximum input duration SHALL be rejected with an "audio too long" error without invoking the model.

#### Scenario: Empty audio
- **WHEN** a request with zero samples is submitted
- **THEN** the result text is empty and no error is raised

#### Scenario: Audio above model limit
- **WHEN** the model's maximum input is 400 seconds and a request contains 401 seconds of audio
- **THEN** the request is rejected with an "audio too long" error

### Requirement: Requests before ready
A transcription request submitted while the status is not `Ready` SHALL be rejected immediately with an error naming the current status.

#### Scenario: Request while loading
- **WHEN** a request is submitted while the status is `Loading`
- **THEN** the request is rejected with an error stating the model is still loading

### Requirement: Failures during transcription
When the native engine fails a transcription while the status is `Ready`, the request SHALL be rejected with a "transcription failed" error, unless the output was only truncated:
- A backend error, such as a lost GPU device or out of memory, with backend `auto` on Vulkan SHALL make the engine reload the model on the CPU backend. Requests queued before the error SHALL run after the reload.
- A backend error with backend `vulkan`, or on the CPU backend, SHALL set the status to `Failed`.
- Output cut off at the model's output limit SHALL be returned as the result text, and the truncation SHALL be logged.
- Any other error SHALL leave the status `Ready`.

#### Scenario: GPU error with auto backend
- **WHEN** the backend is `auto`, the engine is `Ready` on Vulkan and a transcription fails with a backend error
- **THEN** that request is rejected with a "transcription failed" error
- **AND** the status becomes `Loading` and then `Ready` with backend `CPU`
- **AND** a request queued behind the failed one runs on the CPU backend

#### Scenario: GPU error with forced Vulkan
- **WHEN** the backend is `vulkan` and a transcription fails with a backend error
- **THEN** that request is rejected with a "transcription failed" error
- **AND** the status is `Failed`

#### Scenario: Truncated output
- **WHEN** a transcription stops at the model's output limit
- **THEN** the partial text is returned as the result text
- **AND** the status stays `Ready`

#### Scenario: Other transcription error
- **WHEN** a transcription fails with an error that is neither a backend error nor truncated output
- **THEN** the request is rejected with a "transcription failed" error
- **AND** the status stays `Ready`

### Requirement: Load failure handling
When loading fails, the status SHALL become `Failed`, the tray tooltip SHALL say the model failed to load, and the user SHALL receive a notification. If the model file is rejected as invalid, the application SHALL verify the file's SHA-256 against the catalog:
- If the hash does not match, the file SHALL be deleted, so the model is reported as not installed and can be downloaded again.
- If the hash matches, the model file SHALL be kept, and the notification SHALL say that the model is incompatible with this application version.

#### Scenario: Damaged model file
- **WHEN** the model fails to load as invalid and its SHA-256 does not match the catalog
- **THEN** the model file is deleted
- **AND** the tray menu offers **Download model…**

#### Scenario: Intact but unloadable model
- **WHEN** the model fails to load as invalid and its SHA-256 matches the catalog
- **THEN** the file is kept
- **AND** the user is notified that the model is incompatible with this application version

### Requirement: Engine status in tray
The tray icon tooltip SHALL reflect the engine status: "Loading model…" while loading, "Ready (<backend>)" when ready, and "Model failed to load" after a failure.

#### Scenario: Ready tooltip
- **WHEN** the engine becomes `Ready` on the CPU backend
- **THEN** the tray tooltip contains "Ready (CPU)"

### Requirement: Transcription privacy
Audio samples and transcript text SHALL NOT be written to logs or disk. Logs MAY contain audio duration, processing duration, backend, language codes and status codes.

#### Scenario: Transcription is logged
- **WHEN** a transcription completes
- **THEN** the log contains the audio duration and processing time
- **AND** the log does not contain the transcript text

### Requirement: Clean shutdown during transcription
When the application exits while a transcription runs, the engine SHALL cancel it and release the model after the cancelled native call has returned, and the process SHALL still end within 5 seconds. The model SHALL NOT be released while a native call is still running.

#### Scenario: Exit during long transcription
- **WHEN** the user chooses **Exit** while a transcription runs
- **THEN** the process ends within 5 seconds
