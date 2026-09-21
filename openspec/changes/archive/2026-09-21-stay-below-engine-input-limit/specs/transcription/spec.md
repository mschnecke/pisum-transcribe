## MODIFIED Requirements

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
