## MODIFIED Requirements

### Requirement: Hotkey setting
The application SHALL persist the push-to-talk hotkey as `recording.hotkey`: a non-empty set of keys. The default SHALL be the right Ctrl key alone on Windows and the right Command key alone on macOS. A missing, empty or unrecognizable value SHALL be replaced by the platform's default, and a warning SHALL be logged.

#### Scenario: Default hotkey
- **WHEN** no hotkey has been saved on Windows
- **THEN** the push-to-talk hotkey is the right Ctrl key

#### Scenario: Default hotkey on macOS
- **WHEN** no hotkey has been saved on macOS
- **THEN** the push-to-talk hotkey is the right Command key

#### Scenario: Invalid hotkey in settings
- **WHEN** the settings file contains a hotkey with an unknown key name
- **THEN** the hotkey is the platform's default
- **AND** a warning is logged

### Requirement: Global detection
The hotkey SHALL be detected system-wide while any non-elevated application has focus, including when no Pisum Transcribe window is open.

On macOS, the hotkey SHALL be detected only while the Accessibility grant is in effect for the running process, and the application SHALL NOT show macOS's own Accessibility prompt to get it:
- When the process started without the grant, the hotkey SHALL NOT be detected, and no notification SHALL be shown, because the setup window and the **Set up Pisum Transcribe…** menu item already say that the grant is missing. The application SHALL log that push-to-talk waits for the grant.
- When the grant is revoked while the application runs, a notification SHALL say that push-to-talk stopped because Accessibility access was turned off, and that **Set up Pisum Transcribe…** in the menu allows it again.

When the hotkey can't be detected for any other reason, a "Push-to-talk unavailable" notification SHALL say so, on both platforms.

#### Scenario: Hotkey in another application
- **WHEN** a text editor has focus and the user holds the hotkey
- **THEN** a push-to-talk *pressed* signal is raised

#### Scenario: Started without the Accessibility grant on macOS
- **WHEN** Pisum Transcribe starts on macOS without the Accessibility grant
- **THEN** holding the hotkey raises no signal
- **AND** no macOS Accessibility prompt appears
- **AND** no "Push-to-talk unavailable" notification is shown

#### Scenario: Accessibility revoked while running
- **WHEN** Pisum Transcribe runs on macOS with the hotkey working, and the user turns off Pisum Transcribe under System Settings → Privacy & Security → Accessibility
- **THEN** a notification says that push-to-talk stopped because Accessibility access was turned off
- **AND** holding the hotkey raises no signal

### Requirement: Simulated key events ignored
Key events simulated by software, including the application's own text insertion and key remapping tools, SHALL NOT raise, release or cancel the hotkey. On macOS, this SHALL cover every key event that an application posts, including those of automation tools such as Keyboard Maestro, BetterTouchTool and Hammerspoon. A virtual keyboard device, such as the one Karabiner-Elements provides, SHALL count as a keyboard, so a key remapped there to a hotkey key SHALL work as the hotkey.

#### Scenario: Simulated paste with a matching hotkey
- **WHEN** the hotkey is left Ctrl and the application simulates Ctrl+V
- **THEN** no *pressed* signal is raised

#### Scenario: Simulated key while holding
- **WHEN** a *pressed* signal was raised and another application simulates a key press before the user releases the hotkey
- **THEN** no *cancelled* signal is raised

#### Scenario: Posted Command+V on macOS
- **WHEN** the hotkey is right Command and an application posts Command+V on macOS
- **THEN** no *pressed* signal is raised

#### Scenario: Key remapped by a virtual keyboard on macOS
- **WHEN** the hotkey is right Command and Karabiner-Elements maps the Caps Lock key to right Command, and the user holds Caps Lock
- **THEN** a *pressed* signal is raised

### Requirement: Reset on session switch
When the Windows session is locked, unlocked or switched, the hotkey state SHALL reset. On macOS, the same SHALL hold when the screen is locked or the user switches to another user. A hotkey considered held SHALL raise *cancelled*, within 1 second, also while its keys are still physically held.

#### Scenario: Screen locked while holding
- **WHEN** a *pressed* signal was raised and the user locks the workstation before releasing the key
- **THEN** a *cancelled* signal is raised
- **AND** after unlocking, the next press of the hotkey raises *pressed* normally

#### Scenario: Screen locked while holding on macOS
- **WHEN** a *pressed* signal was raised on macOS and the user locks the screen while still holding the hotkey
- **THEN** a *cancelled* signal is raised within 1 second
- **AND** after unlocking, the next press of the hotkey raises *pressed* normally

#### Scenario: Fast user switching on macOS
- **WHEN** a *pressed* signal was raised on macOS and the user switches to another user while still holding the hotkey
- **THEN** a *cancelled* signal is raised within 1 second

### Requirement: Missed release recovery
When the hotkey is considered held but its keys can no longer be observed as held, the hotkey state SHALL reset within 1 second, and a hotkey that raised *pressed* SHALL raise *cancelled*. This covers key releases the application cannot see, for example while an elevated window, the UAC prompt or the lock screen has focus on Windows, and while secure input hides key events on macOS, for example when a password field takes focus.

#### Scenario: Elevated window takes focus while holding
- **WHEN** a *pressed* signal was raised and an elevated window takes focus before the user releases the hotkey
- **THEN** a *cancelled* signal is raised within 1 second
- **AND** the next press of the hotkey raises *pressed* normally

#### Scenario: Password field takes focus while holding on macOS
- **WHEN** a *pressed* signal was raised on macOS, a password field takes focus, and the user then releases the hotkey
- **THEN** a *cancelled* signal is raised within 1 second after the release
- **AND** the next press of the hotkey raises *pressed* normally

#### Scenario: Normal release
- **WHEN** a *pressed* signal was raised and the user releases the hotkey while a non-elevated application has focus
- **THEN** a *released* signal is raised
- **AND** no *cancelled* signal is raised
