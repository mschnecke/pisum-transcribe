## Purpose

Defines how Pisum Transcribe detects speech in dictation audio, so silence is not transcribed and silent recordings do not produce text.

## ADDED Requirements

### Requirement: Voice activity setting
The application SHALL persist `voiceActivity.enabled`, default `true`. The settings window's Dictation section SHALL offer the option as "Trim silence before transcription". A saved change SHALL apply to the next dictation without a restart, and a dictation in progress SHALL keep the setting it started with.

#### Scenario: Default enabled
- **WHEN** no voice activity setting has been saved
- **THEN** voice activity detection is enabled

#### Scenario: Toggle in settings
- **WHEN** the user clears "Trim silence before transcription" and saves
- **THEN** `voiceActivity.enabled` is saved as `false`
- **AND** the next dictation transcribes the full recording

#### Scenario: Change saved during a recording
- **WHEN** voice activity detection is enabled when the user presses the hotkey, and the user saves it as disabled before releasing the hotkey
- **THEN** that dictation is still trimmed
- **AND** the next dictation transcribes the full recording

### Requirement: Silence trimming before transcription
When voice activity detection is enabled, the application SHALL detect speech in a dictation recording before transcription and SHALL pass only the audio from 300 ms before the first detected speech to 300 ms after the last detected speech, clamped to the recording bounds. Audio between the first and last speech, including pauses, SHALL be kept.

#### Scenario: Leading and trailing silence
- **WHEN** a 6-second recording contains 2 seconds of silence, 2 seconds of speech and 2 seconds of silence
- **THEN** the audio passed to transcription is about 2.6 seconds long (speech plus 300 ms on each side)

#### Scenario: Pause inside speech
- **WHEN** a recording contains speech, a 1.5-second pause and more speech
- **THEN** the pause is included in the audio passed to transcription

#### Scenario: Speech at the very start
- **WHEN** speech begins at the first sample of the recording
- **THEN** the trimmed audio starts at the first sample

### Requirement: Silent recordings are not transcribed
When voice activity detection is enabled and no speech is detected in a recording, the application SHALL NOT run transcription, SHALL insert nothing, and SHALL show the "No speech detected" feedback.

#### Scenario: Silence only
- **WHEN** the user holds the hotkey for 3 seconds without speaking
- **THEN** no transcription runs
- **AND** the overlay shows "No speech detected"

#### Scenario: Background noise only
- **WHEN** a recording contains only low-level fan noise
- **THEN** no transcription runs

### Requirement: Detection disabled
When voice activity detection is disabled, the full recording SHALL be passed to transcription unchanged.

#### Scenario: Disabled
- **WHEN** voice activity detection is disabled and the user records 4 seconds including 2 seconds of leading silence
- **THEN** all 4 seconds are passed to transcription

### Requirement: Failure fallback
If voice activity detection cannot run, for example because its model fails to load or inference fails, the application SHALL transcribe the untrimmed recording, SHALL log a warning, and SHALL NOT show an error to the user.

#### Scenario: Detector fails
- **WHEN** voice activity detection throws an error for a recording
- **THEN** the full recording is transcribed and inserted
- **AND** a warning is written to the log

### Requirement: Offline and bundled
Voice activity detection SHALL work without network access and without any download, using only files shipped with the application.

#### Scenario: Offline machine
- **WHEN** the machine has no network connection and a dictation runs with voice activity detection enabled
- **THEN** speech detection runs normally

### Requirement: Detection performance
Speech detection SHALL add no more than 300 ms for a 30-second recording on the target hardware (a ThinkPad E14 Gen 7 class CPU), and the detector SHALL be ready before the first dictation after startup.

#### Scenario: 30-second recording
- **WHEN** voice activity detection processes a 30-second recording on the target laptop
- **THEN** it completes within 300 ms
