## MODIFIED Requirements

### Requirement: Engine status in tray
While no dictation is in progress, the tray icon tooltip SHALL reflect the engine status: "Loading model…" while loading, "Ready (<backend>)" when ready, and "Model failed to load" after a failure. During a dictation, the tooltip SHALL show the dictation state instead, as defined by the dictation capability.

#### Scenario: Ready tooltip
- **WHEN** the engine becomes `Ready` on the CPU backend
- **THEN** the tray tooltip contains "Ready (CPU)"

#### Scenario: Tooltip during a dictation
- **WHEN** the engine is `Ready` on the CPU backend and a recording is running
- **THEN** the tray tooltip does not contain "Ready (CPU)"
- **AND** once the dictation ends, the tray tooltip contains "Ready (CPU)" again
