# transcription Specification

## Purpose

Defines how Pisum Transcribe turns recorded 16 kHz mono audio into text, or into translated text, with a locally loaded speech model that is kept warm and uses the GPU when possible.

## Requirements

### Requirement: Transcription settings
The application SHALL persist these transcription settings with the given defaults:
- `transcription.task`: `translate` or `transcribe`, default `translate`
- `transcription.sourceLanguage`: ISO 639-1 code, default `de`
- `transcription.targetLanguage`: ISO 639-1 code, default `en`
- `transcription.backend`: `auto`, `gpu` or `cpu`, default `auto`

#### Scenario: Defaults on first start
- **WHEN** no transcription settings have been saved
- **THEN** the task is `translate`, the source language is `de`, the target language is `en` and the backend is `auto`

#### Scenario: Backend value of an older settings file
- **WHEN** the settings file was saved by a version that stored the backend `vulkan`
- **THEN** the backend is `gpu`

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
After loading a model, the engine SHALL run one warm-up inference before reporting `Ready` and SHALL discard its output. On the GPU backend, the input SHALL be 10 seconds of low-level noise, so the first real transcription does not pay the one-time GPU pipeline compilation cost. On the CPU backend, which compiles no GPU pipelines, the input SHALL be 1 second of silence.

#### Scenario: First real transcription after warm-up
- **WHEN** the engine reports `Ready` and the first real request arrives
- **THEN** the request runs without triggering a model load or warm-up

#### Scenario: Warm-up input on the GPU
- **WHEN** the model loads on the GPU backend
- **THEN** the warm-up runs on 10 seconds of low-level noise

#### Scenario: Warm-up input on CPU
- **WHEN** the model loads on the CPU backend, including the reload after a GPU backend failure
- **THEN** the warm-up runs on 1 second of silence

#### Scenario: Warm-up output truncated
- **WHEN** the warm-up inference stops at the model's output limit
- **THEN** the warm-up counts as successful
- **AND** the status becomes `Ready`

### Requirement: Model stays loaded
The loaded model SHALL be reused for all transcription requests until the application exits, the model or backend selection changes, a backend error forces a reload on the CPU backend, or the engine returns to the GPU backend after an out-of-memory error. It SHALL NOT be reloaded per request.

#### Scenario: Consecutive transcriptions
- **WHEN** two transcription requests complete one after another
- **THEN** the model was loaded exactly once

### Requirement: Backend selection and fallback
The GPU backend SHALL be Vulkan on Windows and Metal on macOS. With backend `auto`, the engine SHALL use the GPU backend when it is available and loads and warms up successfully. Otherwise it SHALL use the CPU backend. With `gpu`, a GPU backend failure SHALL set the status to `Failed`, with no fallback. With `cpu`, only the CPU backend SHALL be used. A model file rejected as invalid SHALL NOT count as a GPU backend failure. The backend in use SHALL be reported with the `Ready` status and written to the log, by its name: `Vulkan` on Windows or `Metal` on macOS for the GPU backend, and `CPU` for the CPU backend.

#### Scenario: Auto with a working GPU backend
- **WHEN** the backend is `auto` and the GPU backend loads and warms up successfully
- **THEN** the status is `Ready` with the GPU backend

#### Scenario: GPU backend name on Windows
- **WHEN** on Windows the engine is `Ready` on the GPU backend
- **THEN** the backend in use is reported as `Vulkan`

#### Scenario: GPU backend name on macOS
- **WHEN** on macOS the engine is `Ready` on the GPU backend
- **THEN** the backend in use is reported as `Metal`

#### Scenario: Auto with a broken GPU backend
- **WHEN** the backend is `auto` and the GPU backend is unavailable or fails during loading or warm-up
- **THEN** the engine loads the model on the CPU backend
- **AND** the status is `Ready` with backend `CPU`
- **AND** the reason for the fallback is logged

#### Scenario: Forced GPU backend fails
- **WHEN** the backend is `gpu` and the GPU backend fails to load
- **THEN** the status is `Failed`
- **AND** no CPU fallback is attempted

#### Scenario: Invalid model file with auto backend
- **WHEN** the backend is `auto` and the model file is rejected as invalid while loading on the GPU backend
- **THEN** no CPU load is attempted
- **AND** the load failure handling for invalid model files applies

### Requirement: Serialized transcription
The engine SHALL process at most one transcription at a time, including warm-up, and SHALL process queued requests in arrival order.

#### Scenario: Overlapping requests
- **WHEN** a second request arrives while a first request is running
- **THEN** the second request starts only after the first finished
- **AND** results are returned to the matching callers

### Requirement: Audio input contract
Transcription input SHALL be 16 kHz mono 32-bit float samples in the range [-1, 1]. An empty input SHALL return an empty text result without invoking the model. The loaded model's maximum input duration SHALL be 1 second less than the longest input the native engine reports for it, or 400 seconds if the engine reports no limit, so that the engine accepts an input of exactly the maximum input duration. An input longer than the loaded model's maximum input duration SHALL be rejected with an "audio too long" error without invoking the model.

#### Scenario: Empty audio
- **WHEN** a request with zero samples is submitted
- **THEN** the result text is empty and no error is raised

#### Scenario: Audio above model limit
- **WHEN** the model's maximum input is 400 seconds and a request contains 401 seconds of audio
- **THEN** the request is rejected with an "audio too long" error

#### Scenario: Audio at the model limit
- **WHEN** the native engine reports 400 seconds as the longest input for the loaded model
- **THEN** the maximum input duration is 399 seconds
- **AND** a request of 399 seconds of audio is transcribed

#### Scenario: Engine reports no limit
- **WHEN** the native engine reports no limit for the loaded model
- **THEN** the maximum input duration is 400 seconds

### Requirement: Requests before ready
A transcription request submitted while the status is not `Ready` SHALL be rejected immediately with an error naming the current status.

#### Scenario: Request while loading
- **WHEN** a request is submitted while the status is `Loading`
- **THEN** the request is rejected with an error stating the model is still loading

### Requirement: Failures during transcription
When the native engine fails a transcription while the status is `Ready`, the request SHALL be rejected with a "transcription failed" error, unless the output was only truncated or the request succeeds when it is run again on the CPU backend:
- A backend error, such as a lost GPU device or out of memory, with backend `auto` on the GPU backend SHALL make the engine reload the model on the CPU backend and then run the failed request once more on the CPU backend, before the requests queued behind it. The request SHALL return the result of that run, whatever the length of its audio. If that run fails too, the request SHALL be rejected with a "transcription failed" error and SHALL NOT be run again, and the rules below for the CPU backend SHALL decide the status. If the reload fails, the request SHALL be rejected with a "transcription failed" error and the status SHALL become `Failed`. If a model or backend change requested before the reload finished replaces it, the request SHALL be rejected with a "transcription failed" error. Requests queued behind the failed one SHALL run on the CPU backend after it.
- After an out-of-memory error with backend `auto` on the GPU backend, once the reload on the CPU backend has made the engine `Ready` and the failed request has run there or was cancelled by its caller, the engine SHALL return to the GPU backend if both of these hold:
  - The failed request's audio is longer than 10 seconds, the length of the warm-up on the GPU backend, and longer than every request that has completed on the GPU backend since the application started or the saved model or backend last changed. A request whose output was truncated counts as completed.
  - The engine has not returned to the GPU backend since the application started or the saved model or backend last changed, or a request has completed on the GPU backend since its last return.

  Otherwise the engine SHALL stay on the CPU backend. The return SHALL release the model on the CPU backend and load the same model with the same backend selection, fallback and warm-up as the first load. Requests queued behind the failed one SHALL still run on the CPU backend before the return. During the return, the status SHALL stay `Ready` with the backend it had before, and requests SHALL be accepted and validated against the same model. They SHALL wait for the return and then run on the backend it loaded. A model or backend change requested before the return finished SHALL replace it. Requests waiting for a replaced return SHALL run on the model on the CPU backend if it is still loaded, and SHALL otherwise be rejected with an error stating that the model is still loading. If the return fails to load the model on any backend, the status SHALL become `Failed`, and the requests waiting for it SHALL be rejected with an error naming that status.
- After any other backend error with backend `auto` on the GPU backend, such as a lost GPU device, the engine SHALL stay on the CPU backend until the application restarts or the saved model or backend changes.
- A backend error with backend `gpu`, or on the CPU backend, SHALL set the status to `Failed`.
- Output cut off at the model's output limit SHALL be returned as the result text, and the truncation SHALL be logged.
- Any other error SHALL leave the status `Ready`.

#### Scenario: GPU error with auto backend
- **WHEN** the backend is `auto`, the engine is `Ready` on the GPU backend and a transcription fails with a backend error because the GPU device was lost
- **THEN** the status becomes `Loading` and then `Ready` with backend `CPU`
- **AND** the request is run again on the CPU backend and returns its result text
- **AND** a request queued behind the failed one runs on the CPU backend after it
- **AND** the engine does not return to the GPU backend

#### Scenario: GPU error on a long clip
- **WHEN** the backend is `auto`, the engine is `Ready` on the GPU backend and a transcription of 400 seconds of audio fails with a backend error
- **THEN** the request is run again on the CPU backend and returns its result text

#### Scenario: Return to the GPU after running out of memory on a long clip
- **WHEN** the backend is `auto`, the engine is `Ready` on the GPU backend, no request longer than 60 seconds has completed on the GPU backend, and a transcription of 300 seconds fails with an out-of-memory error
- **THEN** the request is run again on the CPU backend and returns its result text
- **AND** the engine then reloads the model on the GPU backend, while the status stays `Ready` with backend `CPU`
- **AND** once the model is loaded, the status is `Ready` with the GPU backend
- **AND** a later request of 10 seconds runs on the GPU backend

#### Scenario: Request during the return to the GPU
- **WHEN** the engine reloads the model on the GPU backend after an out-of-memory error, and a request of 20 seconds is submitted
- **THEN** the request is accepted, not rejected as still loading
- **AND** it runs on the GPU backend once the model is loaded there, and returns its result text

#### Scenario: Request queued behind the one that ran out of memory
- **WHEN** a transcription of 300 seconds fails on the GPU backend with an out-of-memory error, the engine returns to the GPU backend afterwards, and a second request was queued behind the failed one
- **THEN** the second request runs on the CPU backend after the failed one
- **AND** the model is reloaded on the GPU backend after the second request has run

#### Scenario: Out of memory on a clip no longer than a completed one
- **WHEN** the backend is `auto`, a request of 120 seconds has completed on the GPU backend, and later a transcription of 90 seconds fails on the GPU backend with an out-of-memory error
- **THEN** the request is run again on the CPU backend and returns its result text
- **AND** the status stays `Ready` with backend `CPU`

#### Scenario: Out of memory on a short clip
- **WHEN** the backend is `auto`, the engine is `Ready` on the GPU backend, and a transcription of 8 seconds fails with an out-of-memory error
- **THEN** the request is run again on the CPU backend and returns its result text
- **AND** the status stays `Ready` with backend `CPU`

#### Scenario: Out of memory again right after a return
- **WHEN** the engine returned to the GPU backend after an out-of-memory error on a request of 300 seconds, and the next request, of 200 seconds, fails on the GPU backend with an out-of-memory error before any request has completed there
- **THEN** the request is run again on the CPU backend and returns its result text
- **AND** the status stays `Ready` with backend `CPU`

#### Scenario: Out of memory again after a request completed on the GPU
- **WHEN** the engine returned to the GPU backend after an out-of-memory error on a request of 300 seconds, a request of 10 seconds then completed on the GPU backend, and a later request of 300 seconds fails on the GPU backend with an out-of-memory error
- **THEN** the request is run again on the CPU backend and returns its result text
- **AND** the engine returns to the GPU backend again

#### Scenario: Model change resets what completed on the GPU
- **WHEN** a request of 120 seconds has completed on the GPU backend, the saved model changes and the new model becomes `Ready` on the GPU backend, and then a transcription of 90 seconds fails with an out-of-memory error
- **THEN** the request is run again on the CPU backend and returns its result text
- **AND** the engine returns to the GPU backend

#### Scenario: The GPU backend fails during the return
- **WHEN** the engine reloads the model on the GPU backend after an out-of-memory error, and the GPU backend fails during loading or warm-up
- **THEN** the engine loads the model on the CPU backend
- **AND** the status is `Ready` with backend `CPU`
- **AND** a request that waited for the return runs on the CPU backend and returns its result text

#### Scenario: Loading fails during the return
- **WHEN** the engine reloads the model on the GPU backend after an out-of-memory error, a request waits for the return, and loading fails on both backends
- **THEN** the status is `Failed`
- **AND** the waiting request is rejected with an error stating that the model failed to load

#### Scenario: Backend change during the return
- **WHEN** the engine loads the model on the GPU backend after an out-of-memory error, a request waits for the return, and the saved backend changes to `cpu`
- **THEN** the status becomes `Loading` at once, and `Ready` with backend `CPU` once the model is loaded on the CPU backend
- **AND** the waiting request is rejected with an error stating that the model is still loading

#### Scenario: Retry on the CPU fails too
- **WHEN** the backend is `auto`, a transcription fails on the GPU backend with a backend error and its run on the CPU backend fails with a backend error too
- **THEN** the request is rejected with a "transcription failed" error
- **AND** the status is `Failed`
- **AND** the request is not run a third time

#### Scenario: Reload on the CPU fails
- **WHEN** the backend is `auto`, a transcription fails on the GPU backend with a backend error and loading the model on the CPU backend fails
- **THEN** the request is rejected with a "transcription failed" error
- **AND** the status is `Failed`

#### Scenario: Backend change during the failed run
- **WHEN** the backend is `auto`, a transcription runs on the GPU backend, the saved backend changes to `cpu` while it runs, and the run fails with a backend error
- **THEN** the request is rejected with a "transcription failed" error
- **AND** the status becomes `Ready` with backend `CPU` once the newly saved backend is loaded

#### Scenario: GPU error with forced GPU backend
- **WHEN** the backend is `gpu` and a transcription fails with a backend error
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
While no dictation is in progress, the tray icon tooltip SHALL reflect the engine status: "Loading model…" while loading, "Ready (<backend>)" when ready, and "Model failed to load" after a failure. During a dictation, the tooltip SHALL show the dictation state instead, as defined by the dictation capability.

#### Scenario: Ready tooltip
- **WHEN** the engine becomes `Ready` on the CPU backend
- **THEN** the tray tooltip contains "Ready (CPU)"

#### Scenario: Tooltip during a dictation
- **WHEN** the engine is `Ready` on the CPU backend and a recording is running
- **THEN** the tray tooltip does not contain "Ready (CPU)"
- **AND** once the dictation ends, the tray tooltip contains "Ready (CPU)" again

### Requirement: Transcription privacy
Audio samples and transcript text SHALL NOT be written to logs or disk. Logs MAY contain audio duration, processing duration, backend, language codes and status codes.

#### Scenario: Transcription is logged
- **WHEN** a transcription completes
- **THEN** the log contains the audio duration and processing time
- **AND** the log does not contain the transcript text

### Requirement: Clean shutdown during transcription
When the application exits while a transcription runs, the engine SHALL cancel it and release the model after the cancelled native call has returned, and the process SHALL still end within 5 seconds. The model SHALL NOT be released while a native call is still running. When the application exits while the engine reloads the model on the CPU backend to run a failed request again, that request SHALL end as cancelled without waiting for the reload to finish.

#### Scenario: Exit during long transcription
- **WHEN** the user chooses **Exit** while a transcription runs
- **THEN** the process ends within 5 seconds

#### Scenario: Exit during the reload on the CPU
- **WHEN** a transcription failed on the GPU backend with a backend error with backend `auto`, and the user chooses **Exit** while the engine reloads the model on the CPU backend
- **THEN** the failed request ends as cancelled without waiting for the reload to finish
- **AND** the request is not run again
- **AND** the process ends within 5 seconds

### Requirement: Request cancelled by the caller
When the caller of a transcription request cancels it, the request SHALL end as cancelled at once, whether it waits in the queue, runs, or waits while the engine reloads the model on the CPU backend after a GPU backend failure:
- A request that waits in the queue SHALL NOT run.
- A running native call SHALL be aborted at its next abort check. It can keep running until then, and the requests queued behind it SHALL run after it has returned.
- A request whose run failed on the GPU backend SHALL NOT be run again on the CPU backend. The reload SHALL continue and decide the status as it does without a cancel, including the return to the GPU backend after an out-of-memory error.

A cancel SHALL NOT change the engine status or the loaded model, and SHALL NOT affect other requests.

#### Scenario: Cancel a queued request
- **WHEN** a request waits in the queue behind a running one, and its caller cancels it
- **THEN** it ends as cancelled at once and is not run
- **AND** the running request completes normally

#### Scenario: Cancel a running request
- **WHEN** a request runs and its caller cancels it
- **THEN** the request ends as cancelled at once
- **AND** the native run is aborted at its next abort check
- **AND** the status stays `Ready`
- **AND** the next request runs after the aborted run has returned

#### Scenario: Cancel during the reload on the CPU
- **WHEN** the backend is `auto`, a transcription failed on the GPU backend because the GPU device was lost, and its caller cancels it while the engine reloads the model on the CPU backend
- **THEN** the request ends as cancelled without waiting for the reload to finish
- **AND** the status becomes `Ready` with backend `CPU` once the reload has finished
- **AND** the request is not run again

#### Scenario: Cancel during the reload on the CPU after running out of memory
- **WHEN** the backend is `auto`, a transcription of 300 seconds failed on the GPU backend with an out-of-memory error, no request longer than 60 seconds has completed on the GPU backend, and its caller cancels it while the engine reloads the model on the CPU backend
- **THEN** the request ends as cancelled without waiting for the reload to finish
- **AND** once the reload on the CPU backend has finished, the status is `Ready` with backend `CPU`, and the engine reloads the model on the GPU backend
- **AND** once the model is loaded there, the status is `Ready` with the GPU backend
- **AND** the request is not run again

#### Scenario: Cancel the run on the CPU after running out of memory
- **WHEN** the backend is `auto`, a transcription of 300 seconds failed on the GPU backend with an out-of-memory error, no request longer than 60 seconds has completed on the GPU backend, and its caller cancels it while it runs again on the CPU backend
- **THEN** the request ends as cancelled at once
- **AND** the status stays `Ready`
- **AND** once the aborted run has returned, the engine reloads the model on the GPU backend, and the status is `Ready` with the GPU backend once the model is loaded there
- **AND** a request submitted before the aborted run has returned runs on the CPU backend before the reload, and is not rejected

### Requirement: Reload on model or backend change
When the saved model or backend preference changes while the application runs, the engine SHALL reload in the background, with the same backend selection, fallback, warm-up and load failure handling as the first load:
- The status SHALL become `Loading` as soon as the reload is requested, so later requests are rejected as still loading.
- Requests submitted before the reload was requested SHALL complete on the previous model before it is released.
- The previous model SHALL be released before the new one is loaded, so two models are never held in memory at the same time.
- When the model or backend changes again before a reload has finished, the engine SHALL end up with the most recently saved model and backend. The status SHALL stay `Loading` until they are loaded or have failed.
- When the newly selected model is not installed, the engine SHALL NOT try to load it. It SHALL keep its current state and load the model when it becomes installed.

A reload SHALL be possible whatever the engine status, including `Ready` and `Failed`.

#### Scenario: Backend change while ready
- **WHEN** the engine is `Ready` on the GPU backend and the saved backend changes to `cpu`
- **THEN** the status becomes `Loading` and then `Ready` with backend `CPU`
- **AND** the application did not restart

#### Scenario: Request queued before the reload
- **WHEN** a request is being processed on the previous model and a reload is requested
- **THEN** the request completes with its result from the previous model
- **AND** the reload starts afterwards

#### Scenario: Request after the reload started
- **WHEN** a reload is running and a request is submitted
- **THEN** the request is rejected with an error stating the model is still loading

#### Scenario: Change during the startup load
- **WHEN** the model is loading at startup on the GPU backend with backend `auto` and the saved backend changes to `cpu`
- **THEN** the status stays `Loading` until the model is loaded on the CPU backend
- **AND** the status then becomes `Ready` with backend `CPU`

#### Scenario: Selected model not installed yet
- **WHEN** the engine is `Ready` and the saved model changes to a model that is not installed
- **THEN** no load of that model is attempted
- **AND** no load failure is reported
- **AND** once the model becomes installed, the engine loads it
