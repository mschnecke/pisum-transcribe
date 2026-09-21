## MODIFIED Requirements

### Requirement: Failures during transcription
When the native engine fails a transcription while the status is `Ready`, the request SHALL be rejected with a "transcription failed" error, unless the output was only truncated or the request succeeds when it is run again on the CPU backend:
- A backend error, such as a lost GPU device or out of memory, with backend `auto` on Vulkan SHALL make the engine reload the model on the CPU backend and then run the failed request once more on the CPU backend, before the requests queued behind it. The request SHALL return the result of that run, whatever the length of its audio. If that run fails too, the request SHALL be rejected with a "transcription failed" error and SHALL NOT be run again, and the rules below for the CPU backend SHALL decide the status. If the reload fails, the request SHALL be rejected with a "transcription failed" error and the status SHALL become `Failed`. If a model or backend change requested before the reload finished replaces it, the request SHALL be rejected with a "transcription failed" error. Requests queued behind the failed one SHALL run on the CPU backend after it.
- A backend error with backend `vulkan`, or on the CPU backend, SHALL set the status to `Failed`.
- Output cut off at the model's output limit SHALL be returned as the result text, and the truncation SHALL be logged.
- Any other error SHALL leave the status `Ready`.

#### Scenario: GPU error with auto backend
- **WHEN** the backend is `auto`, the engine is `Ready` on Vulkan and a transcription fails with a backend error
- **THEN** the status becomes `Loading` and then `Ready` with backend `CPU`
- **AND** the request is run again on the CPU backend and returns its result text
- **AND** a request queued behind the failed one runs on the CPU backend after it

#### Scenario: GPU error on a long clip
- **WHEN** the backend is `auto`, the engine is `Ready` on Vulkan and a transcription of 400 seconds of audio fails with a backend error
- **THEN** the request is run again on the CPU backend and returns its result text

#### Scenario: Retry on the CPU fails too
- **WHEN** the backend is `auto`, a transcription fails on Vulkan with a backend error and its run on the CPU backend fails with a backend error too
- **THEN** the request is rejected with a "transcription failed" error
- **AND** the status is `Failed`
- **AND** the request is not run a third time

#### Scenario: Reload on the CPU fails
- **WHEN** the backend is `auto`, a transcription fails on Vulkan with a backend error and loading the model on the CPU backend fails
- **THEN** the request is rejected with a "transcription failed" error
- **AND** the status is `Failed`

#### Scenario: Backend change during the failed run
- **WHEN** the backend is `auto`, a transcription runs on Vulkan, the saved backend changes to `cpu` while it runs, and the run fails with a backend error
- **THEN** the request is rejected with a "transcription failed" error
- **AND** the status becomes `Ready` with backend `CPU` once the newly saved backend is loaded

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

### Requirement: Clean shutdown during transcription
When the application exits while a transcription runs, the engine SHALL cancel it and release the model after the cancelled native call has returned, and the process SHALL still end within 5 seconds. The model SHALL NOT be released while a native call is still running. When the application exits while the engine reloads the model on the CPU backend to run a failed request again, that request SHALL end as cancelled without waiting for the reload to finish.

#### Scenario: Exit during long transcription
- **WHEN** the user chooses **Exit** while a transcription runs
- **THEN** the process ends within 5 seconds

#### Scenario: Exit during the reload on the CPU
- **WHEN** a transcription failed on Vulkan with a backend error with backend `auto`, and the user chooses **Exit** while the engine reloads the model on the CPU backend
- **THEN** the failed request ends as cancelled without waiting for the reload to finish
- **AND** the request is not run again
- **AND** the process ends within 5 seconds
