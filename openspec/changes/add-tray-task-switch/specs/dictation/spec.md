## ADDED Requirements

### Requirement: Task switch in the tray menu
The tray menu, the menu bar menu on macOS, SHALL show two radio items above **Settings…**: **Transcribe (<source>)** and **Translate (<source> → <target>)**, where `<source>` and `<target>` are the English names of the saved source and target languages, such as **Transcribe (German)** and **Translate (German → English)**. A language code without a known name SHALL be shown as the code. The item of the saved task SHALL be checked and the other unchecked, and both SHALL reflect the saved settings each time the menu opens. The items SHALL be shown in every tray state, also while no model is installed.

Choosing the unchecked item SHALL save the other task as `transcription.task`; the languages SHALL stay unchanged. Choosing the checked item SHALL change nothing. The next dictation SHALL use the saved task; a dictation in progress SHALL keep the task it started with. An open settings window SHALL show the saved task. When the save fails, the user SHALL receive a notification, the task in effect SHALL stay unchanged, and the menu SHALL show it checked when it opens next.

#### Scenario: Menu shows the saved task
- **WHEN** the settings hold the task `translate`, the source language `de` and the target language `en`, and the user opens the tray menu
- **THEN** the menu shows **Transcribe (German)** unchecked and **Translate (German → English)** checked, above **Settings…**

#### Scenario: Switching to transcribe
- **WHEN** the task is `translate` and the user chooses **Transcribe (German)** in the tray menu
- **THEN** `settings.json` holds the task `transcribe` with the source language `de` and the target language `en`
- **AND** the next dictation inserts the German transcript
- **AND** when the user opens the tray menu again, **Transcribe (German)** is checked

#### Scenario: Choosing the checked item
- **WHEN** the task is `translate` and the user chooses **Translate (German → English)**
- **THEN** the settings file is not written

#### Scenario: Switching during a dictation
- **WHEN** a dictation started with the task `translate` is being transcribed and the user chooses **Transcribe (German)**
- **THEN** that dictation inserts the English translation
- **AND** the next dictation inserts the German transcript

#### Scenario: Open settings window follows
- **WHEN** the settings window is open without edits and the user chooses the other task in the tray menu
- **THEN** the settings window shows the chosen task and **Save** stays disabled

#### Scenario: Save fails
- **WHEN** the settings file can't be written and the user chooses **Transcribe (German)**
- **THEN** the user receives a notification that the mode could not be changed
- **AND** the next dictation still translates
- **AND** when the user opens the tray menu again, **Translate (German → English)** is checked

#### Scenario: No model installed
- **WHEN** no model is installed and the user opens the tray menu
- **THEN** the menu shows both items, and choosing one saves the task

### Requirement: Task in the tray tooltip
The tray tooltip SHALL have a second line that names the saved task and its languages: `Transcribe: <source>` or `Translate: <source> → <target>`, with the same language names as the tray menu, such as `Translate: German → English`. The line SHALL be shown in every tray state, below the state that the requirement "Tray icon states" defines. When the task or a language is saved, from the tray menu or the settings window, the line SHALL change at once, also during a dictation.

#### Scenario: Ready tooltip with the mode
- **WHEN** the engine is ready on the CPU backend and the settings hold the task `translate` from `de` to `en`
- **THEN** the tray tooltip is "Pisum Transcribe – Ready (CPU)" followed by a line "Translate: German → English"

#### Scenario: Tooltip follows a switch
- **WHEN** the user chooses **Transcribe (German)** in the tray menu
- **THEN** the second line of the tray tooltip becomes "Transcribe: German"

#### Scenario: Tooltip follows the settings window
- **WHEN** the user changes the source language to French in the settings window and saves, with the task `transcribe`
- **THEN** the second line of the tray tooltip becomes "Transcribe: French"

#### Scenario: Tooltip while recording
- **WHEN** a dictation is recording with the task `translate` from `de` to `en`
- **THEN** the tray tooltip contains "Recording…" and "Translate: German → English"
