# Plant 3D Automated Publish - Retry Tasks

Tasks for [publish-retry-design.md](publish-retry-design.md). Each task is ticked when implemented and checked
against the design.

## Code changes

- [x] T1 
  ([Run ID in the version tag](publish-retry-design.md#run-id-in-the-version-tag), code change 1)
- [x] T2 
  `DeadlineUtc` (`PLANT_PUBLISH_DEADLINE_UTC`), `DrawingRetries` (`PLANT_PUBLISH_DRAWING_RETRIES`, default 2),
  constant `DeadlineMargin` of 5 minutes. Invalid values are logged as warnings.
  ([Configuration](publish-retry-design.md#configuration-environment-variables), code change 2)
- [x] T3 
- [x] T4 
  match. ([Change detection](publish-retry-design.md#change-detection), code change 4)
- [x] T5 
  ([Failure classification](publish-retry-design.md#failure-classification), code change 5)
- [x] T6 
  ([Result file](publish-retry-design.md#result-file) `ConfigError`)
- [x] T7 
  5s/15s/45s waits, run ID written in the tag. ([Per-drawing retries](publish-retry-design.md#per-drawing-retries),
  [Deadline](publish-retry-design.md#deadline), code change 6)
- [x] T8 
  status order. ([Run state contract](publish-retry-design.md#run-state-contract))
- [x] T9 
- [x] T10 
  `Publish started`, writes the result file in `finally`, closes documents without saving, quits).
  ([Commands](publish-retry-design.md#commands), code change 7)

## Scripts

- [x] T11 
  message" text. (code change 9)
- [x] T12 
  `$PSScriptRoot`. (Decision: `.scr` files cannot resolve relative paths, so an environment variable replaces the
  hard-coded path instead of generating `pub.scr` in `%TEMP%`.)
- [x] T13 
  deadline, attempts, retry decision, summary and [exit codes](publish-retry-design.md#script-exit-codes).

## Verification

- [x] T14 The Plant 3D connector project builds without errors or new warnings.
- [x] T15 Every task above checked against the design.

## Implementation notes and deviations

- `Plant3dPublishOptions` cannot log, because `FileLoggerProvider` depends on it for the run ID. Invalid values are
  collected in `Warnings` and logged by the command when it starts.
- The `run=` field is written only for batch runs (`PLANT_PUBLISH_RUN_ID` set). Interactive runs still get an
  `interactive-{timestamp}` run ID for the log file name.
- Send errors only arrive as a message, so `Plant3dFailureClassifier` matches known markers (timeout, 408, 429, 5xx,
  connection; 401/403 are fatal). Unknown errors, such as conversion errors, are `Fatal`.
- An unexpected exception that ends `-SPECKLEPUBLISH` before results exist writes no result file, so the script
  reports `Crashed` (exit 5) and retries, instead of a misleading empty `Success`.
- "No project drawings matched the mappings" is a `ConfigError`, as nothing can be attempted.
- The result file has an extra optional `error` field holding the config error message.
- Both commands stay in `Plant3dAutomationCommand.cs` to keep the number of files low.
- `PLANT_PUBLISH_SCRIPTS_DIR` is a script-internal variable (see T12).
