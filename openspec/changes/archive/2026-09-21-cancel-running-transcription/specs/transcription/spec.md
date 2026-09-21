## ADDED Requirements

### Requirement: Request cancelled by the caller
When the caller of a transcription request cancels it, the request SHALL end as cancelled at once, whether it waits in the queue, runs, or waits while the engine reloads the model on the CPU backend after a Vulkan backend failure:
- A request that waits in the queue SHALL NOT run.
- A running native call SHALL be aborted at its next abort check. It can keep running until then, and the requests queued behind it SHALL run after it has returned.
- A request whose run failed on Vulkan SHALL NOT be run again on the CPU backend. The reload SHALL continue and decide the status as it does without a cancel.

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
- **WHEN** the backend is `auto`, a transcription failed on Vulkan with a backend error, and its caller cancels it while the engine reloads the model on the CPU backend
- **THEN** the request ends as cancelled without waiting for the reload to finish
- **AND** the status becomes `Ready` with backend `CPU` once the reload has finished
- **AND** the request is not run again
