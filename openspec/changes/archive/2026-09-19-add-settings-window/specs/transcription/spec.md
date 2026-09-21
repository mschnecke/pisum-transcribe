## ADDED Requirements

### Requirement: Reload on model or backend change
When the saved model or backend preference changes while the application runs, the engine SHALL reload in the background, with the same backend selection, fallback, warm-up and load failure handling as the first load:
- The status SHALL become `Loading` as soon as the reload is requested, so later requests are rejected as still loading.
- Requests submitted before the reload was requested SHALL complete on the previous model before it is released.
- The previous model SHALL be released before the new one is loaded, so two models are never held in memory at the same time.
- When the model or backend changes again before a reload has finished, the engine SHALL end up with the most recently saved model and backend. The status SHALL stay `Loading` until they are loaded or have failed.
- When the newly selected model is not installed, the engine SHALL NOT try to load it. It SHALL keep its current state and load the model when it becomes installed.

A reload SHALL be possible whatever the engine status, including `Ready` and `Failed`.

#### Scenario: Backend change while ready
- **WHEN** the engine is `Ready` on Vulkan and the saved backend changes to `cpu`
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
- **WHEN** the model is loading at startup on Vulkan with backend `auto` and the saved backend changes to `cpu`
- **THEN** the status stays `Loading` until the model is loaded on the CPU backend
- **AND** the status then becomes `Ready` with backend `CPU`

#### Scenario: Selected model not installed yet
- **WHEN** the engine is `Ready` and the saved model changes to a model that is not installed
- **THEN** no load of that model is attempted
- **AND** no load failure is reported
- **AND** once the model becomes installed, the engine loads it
