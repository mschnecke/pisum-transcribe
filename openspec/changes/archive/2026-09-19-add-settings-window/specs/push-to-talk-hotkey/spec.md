## ADDED Requirements

### Requirement: Hotkey change at runtime
When a new hotkey is saved, it SHALL take effect immediately, without restarting the application, and the previous hotkey SHALL no longer raise signals. If the previous hotkey had raised *pressed* and is still held when the change takes effect, a *cancelled* signal SHALL be raised.

#### Scenario: New hotkey takes effect
- **WHEN** the hotkey changes from right Ctrl to left Ctrl+left Win
- **THEN** holding left Ctrl+left Win raises *pressed*
- **AND** holding right Ctrl alone raises no signal

#### Scenario: Previous hotkey held during the change
- **WHEN** a *pressed* signal was raised for right Ctrl and the hotkey changes before right Ctrl is released
- **THEN** a *cancelled* signal is raised
- **AND** no *released* signal is raised when right Ctrl is let go

### Requirement: Suspended while configuring
While the user records a new hotkey, the hotkey SHALL be suspended: pressing, releasing or holding it SHALL raise no signal. If the hotkey had raised *pressed* when the suspension starts, a *cancelled* signal SHALL be raised. When the recording of the new hotkey ends, whether it completed, was cancelled, or was abandoned by closing the settings window, the hotkey SHALL work again.

#### Scenario: Hotkey pressed while recording a new one
- **WHEN** the user is recording a new hotkey and presses the current hotkey
- **THEN** no *pressed* signal is raised

#### Scenario: Recording abandoned by closing the window
- **WHEN** the user starts recording a new hotkey and closes the settings window without pressing a key
- **THEN** the next press of the current hotkey raises *pressed*

## MODIFIED Requirements

### Requirement: Key events stay private
The application SHALL use keyboard events only to detect or configure the push-to-talk hotkey. It SHALL NOT keep key events beyond the current hotkey state and, while the user records a new hotkey, the keys held for that recording. It SHALL NOT write keys, key codes or typed characters to disk or logs, except the configured hotkey. Recording a new hotkey SHALL end when the settings window loses focus, so keys typed in other applications are never captured.

#### Scenario: Typing while the app runs
- **WHEN** the user types text in another application while Pisum Transcribe runs
- **THEN** the log contains no entry naming the typed keys
- **AND** no file created by the application contains them

#### Scenario: Cancel by another key
- **WHEN** the hotkey is right Ctrl and the user presses right Ctrl and then C
- **THEN** the logged cancel entry does not name the C key

#### Scenario: Switching away while recording a hotkey
- **WHEN** the user starts recording a new hotkey, switches to another application and types there
- **THEN** the recording of the new hotkey ends without a change
- **AND** the typed keys are not captured
