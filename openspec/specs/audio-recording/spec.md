# audio-recording Specification

## Purpose

Defines how Pisum Transcribe captures the user's voice from the microphone for dictation: format, device behavior, limits, privacy and error reporting.

## Requirements

### Requirement: Output format
A completed recording SHALL provide mono 32-bit float samples at 16,000 Hz with values in the range [-1, 1], independent of the microphone's native format.

#### Scenario: Stereo 48 kHz microphone
- **WHEN** the default recording device's native format is 48 kHz stereo and the user records 2 seconds
- **THEN** the recording contains about 32,000 mono samples, within ±10 %

### Requirement: Default recording device
Recording SHALL use the current Windows default recording device. If the user changes the default recording device while recording, capture SHALL continue from the new default device without ending the recording.

#### Scenario: Default device changed mid-recording
- **WHEN** a recording is running and the user switches the default recording device to a headset
- **THEN** the recording continues
- **AND** subsequent audio comes from the headset

### Requirement: Microphone opened only while recording
The microphone SHALL be opened when a recording starts and released when it stops or is aborted. It SHALL NOT be held open between recordings.

#### Scenario: Idle application
- **WHEN** no recording is running
- **THEN** Windows does not show Pisum Transcribe as using the microphone

### Requirement: Start completes when audio flows
Starting a recording SHALL complete only when the microphone delivers audio that is not silence: audio that Windows does not mark as silence and that is not all digital zeros. Leading silence of either kind SHALL be discarded. If no such audio arrives within 3 seconds, the start SHALL fail with a "microphone not responding" error and release the microphone.

#### Scenario: Slow Bluetooth headset
- **WHEN** the default recording device is a Bluetooth headset that needs 1 second before it delivers audio, and a recording is started
- **THEN** the start completes when the headset delivers audio
- **AND** the recording does not begin with the silence delivered before that

#### Scenario: Microphone array waking up
- **WHEN** the default recording device first delivers digital zeros that Windows does not mark as silence, then sound, and a recording is started
- **THEN** the start completes when the sound arrives
- **AND** the recording does not begin with the zeros

#### Scenario: Microphone delivers no audio
- **WHEN** a recording is started and the default recording device delivers no audio for 3 seconds
- **THEN** the start fails with a "microphone not responding" error
- **AND** the microphone is released

### Requirement: Stop and abort
Stopping a recording SHALL return all audio captured since start. Aborting a recording SHALL discard the captured audio.

#### Scenario: Recording stopped
- **WHEN** the user records for 3 seconds and the recording is stopped
- **THEN** about 3 seconds of audio are returned

#### Scenario: Recording aborted
- **WHEN** a running recording is aborted
- **THEN** no audio is returned
- **AND** the microphone is released

### Requirement: Maximum duration
A recording SHALL stop automatically when it reaches the maximum duration given at start, release the microphone, keep the captured audio, and signal that the limit was reached.

#### Scenario: Limit reached
- **WHEN** a recording is started with a maximum of 400 seconds and continues for 400 seconds
- **THEN** capture stops
- **AND** the microphone is released
- **AND** a "maximum duration reached" signal is raised
- **AND** the 400 seconds of audio are available

### Requirement: Microphone access blocked
When Windows privacy settings deny the application microphone access, starting a recording SHALL fail with an error explaining that microphone access is blocked. The error SHALL point to Settings › Privacy & security › Microphone and say that microphone access must be allowed for desktop apps and for Pisum Transcribe.

#### Scenario: Desktop apps not allowed
- **WHEN** "Let desktop apps access your microphone" is off and a recording is started
- **THEN** the start fails with the "microphone access blocked" error pointing to the Microphone privacy settings

#### Scenario: Microphone access off for the device
- **WHEN** "Microphone access" is off for the whole device and a recording is started
- **THEN** the start fails with the "microphone access blocked" error pointing to the Microphone privacy settings

### Requirement: Microphone muted
When the default recording device is muted in Windows, for example with the laptop's microphone mute key, starting a recording SHALL fail with a "microphone muted" error that tells the user to unmute the microphone. The microphone SHALL NOT be left open.

#### Scenario: Mute key on
- **WHEN** the default recording device is muted and a recording is started
- **THEN** the start fails with the "microphone muted" error
- **AND** Windows does not show Pisum Transcribe as using the microphone

### Requirement: No microphone available
When no recording device is available, starting a recording SHALL fail with a "no microphone found" error.

#### Scenario: No input device
- **WHEN** no recording device is connected or enabled and a recording is started
- **THEN** the start fails with a "no microphone found" error

### Requirement: Microphone lost during recording
When capture ends unexpectedly during a recording and no default recording device remains, the recording SHALL end with a "microphone disconnected" error, discard the captured audio, and release the microphone without waiting for a stop or abort. Stopping that recording afterwards SHALL fail with the same error. A new recording SHALL be possible without aborting the failed one first.

#### Scenario: USB microphone unplugged
- **WHEN** the only microphone is unplugged during a recording
- **THEN** the recording ends with a "microphone disconnected" error

#### Scenario: Stop after the microphone was lost
- **WHEN** a recording ended with a "microphone disconnected" error and the recording is then stopped
- **THEN** the stop fails with the "microphone disconnected" error
- **AND** no audio is returned

#### Scenario: New recording after the microphone was lost
- **WHEN** a recording ended with a "microphone disconnected" error, a microphone is connected again, and a new recording is started without aborting the failed one
- **THEN** the new recording starts

### Requirement: Audio stays in memory
Captured audio SHALL be kept only in memory and SHALL NOT be written to disk or logs.

#### Scenario: Recording completes
- **WHEN** a recording is stopped
- **THEN** no audio file has been created by the application
