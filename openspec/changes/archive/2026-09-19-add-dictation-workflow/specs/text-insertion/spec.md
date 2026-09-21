## ADDED Requirements

### Requirement: Pending clipboard restore
An insertion SHALL count as finished once the text is delivered or the fallback is reported. The clipboard restore after a paste SHALL NOT delay it. An insertion that starts while the restore of a previous paste is still pending SHALL wait for that restore before it reads or changes the clipboard, so the restore puts back the user's content and not the previous transcript. When the application exits while a restore is pending, the restore SHALL complete before the process ends, within the application's exit time limit.

#### Scenario: Next paste before the restore
- **WHEN** the clipboard contains "invoice 4711", a transcript is pasted with restore enabled, and a second transcript is ready to paste 300 ms later
- **THEN** the second insertion waits until the first restore has run
- **AND** after the second restore the clipboard contains "invoice 4711"

#### Scenario: Exit right after a paste
- **WHEN** the clipboard contains "invoice 4711", a transcript is pasted with restore enabled, and the user chooses **Exit** 200 ms later
- **THEN** the clipboard contains "invoice 4711" after the process has ended
