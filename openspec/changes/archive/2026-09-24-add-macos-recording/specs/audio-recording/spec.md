## MODIFIED Requirements

### Requirement: Default recording device
Recording SHALL use the current default recording device: the Windows default recording device, or the macOS default input device. If the user changes the default recording device while recording, capture SHALL continue from the new default device without ending the recording.

#### Scenario: Default device changed mid-recording
- **WHEN** a recording is running and the user switches the default recording device to a headset
- **THEN** the recording continues
- **AND** subsequent audio comes from the headset

#### Scenario: Input device changed mid-recording on macOS
- **WHEN** a recording is running on macOS with the built-in microphone, and the user chooses AirPods as the input device in System Settings → Sound
- **THEN** the recording continues
- **AND** subsequent audio comes from the AirPods

### Requirement: Microphone opened only while recording
The microphone SHALL be opened when a recording starts and released when it stops or is aborted. It SHALL NOT be held open between recordings.

#### Scenario: Idle application
- **WHEN** no recording is running
- **THEN** Windows does not show Pisum Transcribe as using the microphone

#### Scenario: Idle application on macOS
- **WHEN** no recording is running on macOS
- **THEN** the orange microphone indicator in the menu bar is off for Pisum Transcribe

### Requirement: Start completes when audio flows
Starting a recording SHALL complete only when the microphone delivers audio that is not silence: audio that Windows does not mark as silence and that is not all digital zeros. On macOS, which marks no audio as silence, only digital zeros SHALL count as silence. Leading silence of either kind SHALL be discarded. If no such audio arrives within 3 seconds, the start SHALL fail with a "microphone not responding" error and release the microphone.

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

#### Scenario: AirPods on macOS
- **WHEN** the default input device on macOS is a pair of AirPods, and a recording is started
- **THEN** the start completes when the AirPods deliver audio, within 3 seconds
- **AND** the recording does not begin with the digital zeros delivered before that

### Requirement: Microphone access blocked
When Windows privacy settings deny the application microphone access, starting a recording SHALL fail with an error explaining that microphone access is blocked. The error SHALL point to Settings › Privacy & security › Microphone and say that microphone access must be allowed for desktop apps and for Pisum Transcribe.

On macOS, when the microphone permission is denied, restricted or not yet asked, starting a recording SHALL fail at once with the "microphone access blocked" error, before the microphone is opened, because macOS would deliver digital zeros and no error. Starting a recording SHALL NOT ask for the permission. The error SHALL point to System Settings → Privacy & Security → Microphone.

#### Scenario: Desktop apps not allowed
- **WHEN** "Let desktop apps access your microphone" is off and a recording is started
- **THEN** the start fails with the "microphone access blocked" error pointing to the Microphone privacy settings

#### Scenario: Microphone access off for the device
- **WHEN** "Microphone access" is off for the whole device and a recording is started
- **THEN** the start fails with the "microphone access blocked" error pointing to the Microphone privacy settings

#### Scenario: Microphone denied on macOS
- **WHEN** Pisum Transcribe is turned off under System Settings → Privacy & Security → Microphone and a recording is started
- **THEN** the start fails at once with the "microphone access blocked" error pointing to System Settings → Privacy & Security → Microphone
- **AND** the orange microphone indicator doesn't turn on

#### Scenario: Microphone not asked yet on macOS
- **WHEN** the microphone permission wasn't asked yet on macOS and a recording is started
- **THEN** the start fails with the "microphone access blocked" error
- **AND** macOS doesn't show its microphone prompt

### Requirement: Microphone muted
When the default recording device is muted in Windows, for example with the laptop's microphone mute key, or the default input device reports itself as muted on macOS, starting a recording SHALL fail with a "microphone muted" error that tells the user to unmute the microphone. The microphone SHALL NOT be left open. On macOS, the check SHALL run before the microphone is opened, for input devices that report a mute state.

#### Scenario: Mute key on
- **WHEN** the default recording device is muted and a recording is started
- **THEN** the start fails with the "microphone muted" error
- **AND** Windows does not show Pisum Transcribe as using the microphone

#### Scenario: Muted input device on macOS
- **WHEN** the default input device on macOS reports itself as muted and a recording is started
- **THEN** the start fails at once with the "microphone muted" error
- **AND** the orange microphone indicator doesn't turn on
