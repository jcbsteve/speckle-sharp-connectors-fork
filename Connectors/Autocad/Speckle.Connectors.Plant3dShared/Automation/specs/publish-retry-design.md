# Plant 3D Automated Publish - Retry and Run State Design

## Context

`pub.ps1` launches Plant 3D with `pub.scr`, which loads `pub.lsp`, opens the project and runs `SPECKLEPUBLISH`
(`Plant3dAutomationCommand`). Today:

- The command is `async void`, so LISP's `(command "SPECKLEPUBLISH")` returns before publishing completes.
- Plant 3D stays open after the run, so the script cannot tell when the run finished.
- Results go to one shared, append-only `%LOCALAPPDATA%\Speckle\Logs\plant3d-automation.log` file. Runs cannot be
  told apart, and the result can only be found by reading free text.
- `Plant3dAutomationPublishResult` already records results per drawing, but they are not classified as transient or fatal.
- With `PLANT_PUBLISH_FORCE`, `Plant3dDrawingChangeDetector` returns `Forced` without querying the server, so a
  retry would publish every drawing again.

## Requirements (agreed)

| # | Requirement |
|---|-------------|
| R1 | Every drawing should succeed. If that is not possible, a partial publish is better than no publish. |
| R2 | Timeouts are transient and are retried. Permission, conversion and file-lock errors are fatal and are not retried. |
| R3 | The run must report which drawings failed. Add as few new files as possible. |
| R4 | The total run timeout defaults to 2 hours and can be configured. |
| R5 | A retry does not publish a drawing again if it was already published, including in `Force` mode. |
| R6 | Runs normally start from Windows Task Scheduler with the user logged on. There is no CI/CD. |
| R7 | No new settings JSON file for now. `PLANT_PROJECT` and `PLANT_DRAWING_MAPPINGS` stay as environment variables. |
| R8 | `pub.lsp` keeps opening the project with `-openproject`, including the double-open workaround. |

## Decision summary

1. **Hybrid retry**
   - **The command retries each drawing**, for transient failures only. The retry loop runs inside the process,
     so it is cheap.
   - **The script retries the whole process**: it relaunches Plant 3D after a crash, a hang, or a run that ends
     with failures that can still be retried.
2. **Run ID in the version tag.** The run ID goes into the fingerprint tag in the version message. Retries then use
   the normal change detection, with no list of drawing GUIDs to pass between attempts.
3. **Two commands.** One command is interactive and leaves Plant 3D open. The other is for batch runs and closes
   Plant 3D when it finishes.
4. **A separate result file.** It holds formatted JSON and is written for each attempt. The log file stays
   free text, for people to read.
5. **The process exit tells the script that the run finished, not whether it succeeded.** The script reads the
   result file after Plant 3D exits. The AutoCAD exit code is not used.
6. **The script's exit code is the overall result**, so Task Scheduler's "Last Run Result" shows the outcome.

## Commands

| Command | Use | Prompts | Closes Plant 3D |
|---------|-----|---------|-----------------|
| `SPECKLEPUBLISH` | Interactive | Yes. Asks for the mappings path, defaulting to `PLANT_DRAWING_MAPPINGS`, and asks whether to force | No |
| `-SPECKLEPUBLISH` | Batch (`pub.lsp`) | No. Reads the environment variables only | Yes, after it writes the result file |

- The dash prefix follows AutoCAD's convention for the command-line form of a command.
- Both commands call the same `Plant3dAutomationService.PublishAsync`. They differ only in how they collect options
  and in what they do after publishing.
- The batch command closes Plant 3D itself because it is `async void`. A LISP `(command "QUIT")` would run before
  publishing finishes.
- No `PLANT_PUBLISH_EXIT_ON_COMPLETE` variable is needed.

## Failure classification

Add `FailureKind` to `Plant3dAutomationPublishResult`:

| FailureKind | Examples | Retried by command | Retried by script |
|-------------|----------|--------------------|-------------------|
| `None` | Published, unchanged, already published in this run | - | - |
| `Transient` | `TimeoutException`, `HttpRequestException`, HTTP 408/429/5xx, server check failed because of network | Yes | Yes |
| `Fatal` | HTTP 401/403, conversion exception, no objects, drawing not opened or locked, invalid mapping | No | No |
| `Cancelled` | Deadline reached before the drawing was attempted | - | Yes |

Classification happens in one place, `Plant3dFailureClassifier`, so the rules are easy to change.

Note on locks: drawings in the collaboration cache are local copies, and both hashing and opening only need read
access. A lock is only expected when another Plant 3D session on the same machine has the drawing open, and
retrying will not fix that, so locks are treated as `Fatal`.

## Run ID in the version tag

The fingerprint tag gets an optional `run` field:

```
[speckle-plant3d sha256=<HEX> schema=1 run=<RunId>]
```

- `VersionFingerprint` gets `string? RunId`. The pattern treats `run=` as optional, so tags written before this
  change still parse.
- `Plant3dPublishSchema.VERSION` does **not** change. The schema describes the converted content, not the tag.
- `run` is written only when a run ID is set. Interactive runs leave it out.

### Change detection

| Mode | Latest version on the server | Decision |
|------|------------------------------|----------|
| Normal | hash matches, schema matches | `Unchanged`, skip |
| Normal | otherwise | publish (`NeverPublished` / `SchemaChanged` / `ContentChanged`) |
| `Force` | `run` equals the current run ID **and** hash matches | `AlreadyPublishedInRun`, skip |
| `Force` | otherwise | `Forced`, publish |

- In `Force` mode the detector now always queries the server for the latest version, the same as the normal path.
  If the server check fails, the drawing fails as `Transient`.
- `drawing.AlwaysPublish` works the same as `Force` for that drawing.
- `AlreadyPublishedInRun` counts as success and appears in the summary counts.

As a result, every attempt in a run uses the same settings, and `PLANT_PUBLISH_FORCE` keeps its value on retries.

## Configuration (environment variables)

Values set with `$env:` in `pub.ps1` are passed straight to the `acad.exe` process the script starts, so no restart
is needed.

| Variable | Set by | Default | Purpose |
|----------|--------|---------|---------|
| `PLANT_PROJECT` | user, in `pub.ps1` | required | Project.xml path. Read by `pub.lsp` only |
| `PLANT_DRAWING_MAPPINGS` | user, in `pub.ps1` | required | Mappings JSON. Read by the C# code |
| `PLANT_PUBLISH_FORCE` | user, in `pub.ps1` | `false` | Existing. Publish every mapped drawing once per run |
| `PLANT_PUBLISH_RUN_ID` | script | generated by the command | `{ProjectName}-{yyyyMMddTHHmmssZ}`. Shared by every attempt. Used in the version tag and in file names |
| `PLANT_PUBLISH_DEADLINE_UTC` | script | none (no deadline) | ISO-8601 time. The script's hard deadline. The command stops starting new drawings `DeadlineMargin` before it |
| `PLANT_PUBLISH_DRAWING_RETRIES` | user, optional | `2` | Extra tries per drawing inside the command for `Transient` failures. `0` turns off retries in the command |
| `PLANT_PUBLISH_SCRIPTS_DIR` | script | `$PSScriptRoot` | Folder of `pub.lsp`. Read by the static `pub.scr`, which cannot resolve relative paths |

There is no attempt-number variable. Only the script needs the attempt number, and it reports it on its console output.

### Run ID

- The script builds the run ID as `{ProjectName}-{yyyyMMddTHHmmssZ}`, for example
  `Plant3D_Speckle-20250101T020000Z`.
- `ProjectName` is the name of the folder that holds `PLANT_PROJECT` (`Project.xml`). Characters other than
  `A-Z a-z 0-9 _ -` are replaced with `_`, so the value is safe in file names and in the version tag.
- The C# code treats the run ID as an opaque string. It does not need the project name.
- Interactive runs, where `PLANT_PUBLISH_RUN_ID` is not set, generate `interactive-{yyyyMMddTHHmmssZ}`.

### Deadline

- The script sets `PLANT_PUBLISH_DEADLINE_UTC` to its own hard deadline, the time it will stop Plant 3D.
- `Plant3dPublishOptions` has a code-set `DeadlineMargin` of 5 minutes. It is not configurable. The command's
  cut-off is `DeadlineUtc - DeadlineMargin`.
- The margin gives the drawing being sent time to finish and be tagged, and gives the command time to write the
  result file before the script stops Plant 3D. The result file then shows how many drawings were processed before
  the deadline.
- If a send takes longer than the margin, the script still stops Plant 3D at the hard deadline. That attempt has no
  result file and is reported as a timeout. Retries remain safe because of change detection.
- The command checks the cut-off before each drawing and before each retry of a drawing. A send that has already
  started is never cancelled.
- Drawings that are not started because of the deadline are reported as `Cancelled`, and the script can retry them
  in the next run.

### Per-drawing retries

- Each drawing is tried up to `1 + PLANT_PUBLISH_DRAWING_RETRIES` times.
- Only `Transient` failures are retried. This covers the server check in change detection and the publish itself.
- The waits between tries are 5s, then 15s, then 45s (multiplied by 3 each time).
- The command stops retrying when the cut-off is reached. The drawing is then reported with its last `Transient`
  failure.
- An invalid value (not a whole number, or negative) is logged as a warning and the default is used.

`pub.ps1` parameters, which can be set in the Task Scheduler action's arguments:

| Parameter | Default |
|-----------|---------|
| `-TimeoutMinutes` | `120` (total for all attempts) |
| `-MaxAttempts` | `3` |
| `-RetryDelaySeconds` | `60`, doubled after each attempt |

## Run state contract

### Files

```
%LOCALAPPDATA%\Speckle\Logs\plant3d-automation-{RunId}.log           free text, appended by every attempt
%LOCALAPPDATA%\Speckle\Logs\plant3d-automation-{RunId}.result.json   overwritten by each attempt
```

- `RunId` includes the project name (see [Run ID](#run-id)), for example
  `plant3d-automation-Plant3D_Speckle-20250101T020000Z.log`, so runs of different projects in the same night are
  easy to tell apart.
- The log file is appended to by every attempt in the run.
- Only the command writes to the log. At the start of each attempt it logs a `Publish started` line with the run
  ID, force flag, deadline and drawing retries. Attempts are told apart by this line and the timestamps.
- The script reports attempt numbers on its own console output only.
- `FileLoggerProvider.CreateLogFilePath()` takes the path from the run ID. This resolves the existing TODO.
- The result file is written once per attempt, after all drawings are processed or the cut-off is reached. It is
  not written after each drawing. If the attempt is stopped part-way through, there is no result file, and the log
  shows how far it got.
- The result file is written by writing `*.result.json.tmp` and then renaming it, so the script never reads a
  half-written file.
- The script deletes the result file before each attempt. If it is missing after Plant 3D exits, the attempt
  crashed or hung.

### Result file

```json
{
  "runId": "Plant3D_Speckle-20250101T020000Z",
  "status": "Partial",
  "force": true,
  "startedUtc": "2025-01-01T02:10:00Z",
  "completedUtc": "2025-01-01T02:41:12Z",
  "counts": { "published": 3, "unchanged": 12, "alreadyPublishedInRun": 2, "failed": 1, "cancelled": 1 },
  "failures": [
    { "guid": "...", "path": "...\\A-101.dwg", "failureKind": "Fatal", "error": "Conversion ..." },
    { "guid": "...", "path": "...\\A-102.dwg", "failureKind": "Cancelled", "error": "Deadline reached" }
  ]
}
```

- `failures` lists **only the drawings that did not succeed**, including `Cancelled` ones. The counts cover the rest.
- `counts` each drawing is counted exactly once.
- `counts.failed` counts `Transient` and `Fatal` failures.
- `counts.cancelled` counts drawings not started because the cut-off was reached.
- `status`: (evaluated in this order, first match wins):
  - `ConfigError`: the mappings, account or project are invalid, so nothing was attempted
  - `Cancelled`: the deadline was reached and at least one drawing was cancelled
  - `Success`: no failures or cancellations, all drawings processed before the deadline.
  - `Partial`: at least one drawing succeeded and at least one failed.
  - `Failed`: no drawing succeeded and at least one failed.
- Because a retry skips drawings that already published in this run, or that are unchanged, the last attempt's
  file is a complete picture of the run. No history of earlier attempts is needed.

### Script exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Partial. Only fatal failures remain |
| 2 | Failed. Only fatal failures remain |
| 3 | Retryable failures remain after all attempts were used |
| 4 | ConfigError |
| 5 | Process crashed - no result file |
| 124 | Total timeout reached |
| 140 | Processing cut-off reached |

The exit code comes from the final attempt only, checked in this order: `Timeout` (script deadline) = 124,
`Crashed` = 5, `ConfigError` = 4, `Cancelled` = 140, `Success` = 0, any `Transient` failure left = 3,
`Partial` = 1, `Failed` = 2.

## Code changes

1. `VersionFingerprint`: add the optional `RunId` and update `ToTag` and `TryParse`. The `run` pattern is
   `[A-Za-z0-9_-]+`.
2. `Plant3dPublishOptions` reads the environment variables. It is the only class that does, apart from
   `Plant3dDrawingModelMappingResolver`:
   - existing: `Force` from `PLANT_PUBLISH_FORCE` (unchanged).
   - new: `RunId` from `PLANT_PUBLISH_RUN_ID`, or the generated `interactive-{timestamp}` value.
   - new: `DeadlineMargin` (`TimeSpan`), a code-set constant of 5 minutes. No environment variable.
   - new: `DeadlineUtc` (`DateTimeOffset?`) from `PLANT_PUBLISH_DEADLINE_UTC`. A value that does not parse is logged
     as a warning and ignored.
   - new: `DrawingRetries` (`int`, default `2`) from `PLANT_PUBLISH_DRAWING_RETRIES`.
   - `PLANT_DRAWING_MAPPINGS` is still read by `Plant3dDrawingModelMappingResolver` (unchanged).
   - `PLANT_PROJECT` is still read by `pub.lsp` only (unchanged).
   - Removed from the design: `PLANT_PUBLISH_ATTEMPT`, `PLANT_PUBLISH_ONLY`, `PLANT_PUBLISH_EXIT_ON_COMPLETE`.
3. `PublishReason`: add `AlreadyPublishedInRun`.
4. `Plant3dDrawingChangeDetector`: query the server in `Force` mode too, and apply the rules in the change detection
   table.
5. `Plant3dAutomationPublishResult`: add `FailureKind`. Add `Plant3dFailureClassifier`.
6. `Plant3dAutomationService.PublishAsync`:
   - Before each drawing, check the cut-off (`DeadlineUtc - DeadlineMargin`). If it has passed, record `Cancelled` for
     every remaining drawing, then stop.
   - Retry `Transient` failures up to `DrawingRetries` times, waiting 5s, 15s, 45s between tries. Check the cut-off
     before each retry. Sends that have started are not cancelled.
   - Write `RunId` into the version tag (`TagVersionAsync`).
7. Commands:
   - Split `Plant3dAutomationCommand` into `SPECKLEPUBLISH` (interactive) and `-SPECKLEPUBLISH` (batch).
   - Batch command:
     - Log a `Publish started` line (run ID, force, deadline, drawing retries).
     - Write the result file from `finally`.
     - Close Plant 3D after the result file is written.
8. `FileLoggerProvider`: name the log file from the run ID.
9. `pub.lsp`: call `-SPECKLEPUBLISH`. Remove the "check for published version message" text.

## `pub.ps1` wrapper flow

```
param TimeoutMinutes=120, MaxAttempts=3, RetryDelaySeconds=60

projectName = sanitize(folder name of PLANT_PROJECT)
runId       = "{projectName}-{UTC yyyyMMddTHHmmssZ}"
deadline    = now + TimeoutMinutes
logPath     = %LOCALAPPDATA%\Speckle\Logs\plant3d-automation-{runId}.log
resultPath  = %LOCALAPPDATA%\Speckle\Logs\plant3d-automation-{runId}.result.json
set env: PLANT_PUBLISH_RUN_ID = runId
set env: PLANT_PUBLISH_DEADLINE_UTC = deadline       # the command applies its own 5 minute margin
# PLANT_PROJECT, PLANT_DRAWING_MAPPINGS, PLANT_PUBLISH_FORCE, PLANT_PUBLISH_DRAWING_RETRIES set by the user

for attempt in 1..MaxAttempts:
    write "Attempt {attempt} of {MaxAttempts}" to console
    delete resultPath
    process = start acad.exe (/product PLNT3D /b pub.scr) -PassThru
    wait for exit, up to (deadline - now)
    if still running: kill this process tree only; status = Timeout; break

    if resultPath missing: status = Crashed; continue to retry      # retryable
    result = read resultPath
    if result.status in Success, ConfigError: break
    if no failure has failureKind in Transient, Cancelled: break    # only fatal failures remain

    delay = RetryDelaySeconds * 2^(attempt-1)
    if now + delay >= deadline: break
    sleep delay

write summary (status, failures, log and result paths) to stdout
exit with the code that matches the final status
```

Notes:

- Only stop the `acad.exe` process that the script started, using the saved PID. The user may have other Plant 3D
  sessions open.
- Build the paths in `pub.ps1`/`pub.scr` from `$PSScriptRoot` instead of the hard-coded
  `speckle-sharp-connectors-jacobs` folder. `pub.scr` can be generated in `%TEMP%` by the script, because `.scr`
  files cannot resolve relative paths.
- Task Scheduler: run with "Run only when user is logged on", because Plant 3D needs a desktop session. Set the
  task's own "Stop the task if it runs longer than" value above `-TimeoutMinutes`, so the script's timeout applies
  first.

## Open points

- Confirm how quitting behaves in collaboration (ACC) projects. Make sure no check-in or save prompt blocks the exit.
  If one can, the script stops the process after a short grace period once the result file exists.
- The tag is not written when the version is created. After a successful publish, `TagVersionAsync` updates the
  version message with `Version.Update`. If that update fails, the failure is only logged as a warning, and the
  version has no fingerprint or `run=` tag. On a retry in the same run, change detection then publishes the drawing
  again. This is harmless (an extra version). The failed tag update should stay a warning, and count as success
  (current behaviour).
- The interactive `SPECKLEPUBLISH` prompts (mappings path, force) need defining. Out of scope: keep today's
  behaviour of SPECKLEPUBLISH for the interactive command.
