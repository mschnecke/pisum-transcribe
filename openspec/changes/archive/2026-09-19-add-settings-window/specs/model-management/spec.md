## ADDED Requirements

### Requirement: Model deletion
An installed catalog model that is not the selected model SHALL be deletable. Deleting SHALL remove the model file, so the model is reported as not installed. The selected model SHALL NOT be deletable. If the model file cannot be deleted, for example because it is still in use, the model SHALL stay installed and the failure SHALL be reported to the user.

#### Scenario: Delete an unused model
- **WHEN** `canary-1b-v2-q4_k_m` is installed, it is not the selected model, and the user deletes it
- **THEN** its model file is removed
- **AND** the model is reported as not installed

#### Scenario: Selected model cannot be deleted
- **WHEN** a request to delete the selected model is made
- **THEN** the request is rejected
- **AND** the model file is kept

#### Scenario: Model file in use
- **WHEN** the user deletes a model whose file cannot be removed because it is in use
- **THEN** the model is still reported as installed
- **AND** the user is told that the model could not be deleted

### Requirement: One download per model
At most one download of a model SHALL run at a time. Starting a download of a model that is already downloading SHALL be rejected with an error saying that the model is already downloading. The running download SHALL continue unaffected.

#### Scenario: Same model started twice
- **WHEN** a download of `canary-180m-flash-q8_0` is running and another download of the same model is started
- **THEN** the second download is rejected with an error saying the model is already downloading
- **AND** the first download continues
- **AND** the model is reported as installed once the first download succeeds
