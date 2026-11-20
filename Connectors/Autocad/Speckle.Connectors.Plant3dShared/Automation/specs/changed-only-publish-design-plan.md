# 🎯 Plant 3D Publish Retry and Run State

Implements `Automation\specs\publish-retry-design.md`. All changes stay under `Connectors\Autocad\Speckle.Connectors.Plant3dShared\Automation`. Spec-driven workflow: first write a task checklist that maps to the spec, implement each task, tick it off, and then check every item against the spec. No unit tests. Build only the Plant 3D project. Do not build the whole solution.

## Decisions (from the user)
- `-SPECKLEPUBLISH` writes the result file, closes all documents without saving, then calls `Application.Quit()`.
- The task checklist goes in a new file, `specs\publish-retry-tasks.md`.
- Config errors (invalid or missing mappings, no account, no project drawings, invalid mapping) throw a new `Plant3dConfigException`. The command catches it and writes `status = ConfigError`.
- The scripts are in `Automation\Scripts`. Keep `pub.scr` as a static file and do not generate it. Build `pub.ps1` paths from `$PSScriptRoot`. The hard-coded `pub.lsp` path in `pub.scr` must be fixed. Either `pub.ps1` passes the folder via an env var read by `pub.lsp`, or the path stays documented. Choose the simplest option and record it in the tasks file.

## Key existing code
- `Commands\Plant3dAutomationCommand.cs`: a single `async void Command()` on `SpecklePublish`. It builds the DI container and logs each result and a summary.
- `Services\Plant3dAutomationService.cs`: `PublishAsync()` returns `[]` early on config problems. It loops over drawings, calls `changeDetector.EvaluateAsync`, then `PublishDrawingAsync`, then `TagVersionAsync`.
- `Plant3dDrawingChangeDetector`, `VersionFingerprint`, `PublishReason`, `Plant3dPublishOptions`, `Plant3dAutomationPublishResult`, `FileLoggerProvider`: modify these in place. Find each one by name inside Automation.

## Risks
- Quitting in collaboration (ACC) projects may show save or check-in prompts. Close documents with discard to avoid them. The script's hard deadline is the fallback.
- Reuse the shared container setup in both commands. Do not duplicate it.

**Last Updated**: 2026-11-20 12:25:51

## 📝 Plan Steps
-  **Create `specs\publish-retry-tasks.md`. Add a checklist with one item for each of spec code changes 1 to 9, the result file, the log naming, `pub.ps1`, and `pub.lsp`/`pub.scr`. Each item references its spec section.**
-  **Update `VersionFingerprint`. Add `string? RunId`. `ToTag` writes ` run=<id>` only when it is set. `TryParse` accepts an optional `run=([A-Za-z0-9_-]+)`, so old tags still parse.**
-  **Update `Plant3dPublishOptions`. Add `RunId` from `PLANT_PUBLISH_RUN_ID`, or `interactive-{yyyyMMddTHHmmssZ}` when it is not set. Add `DeadlineUtc` (`DateTimeOffset?`) from `PLANT_PUBLISH_DEADLINE_UTC`, logging a warning and ignoring the value when it does not parse. Add `DrawingRetries` from `PLANT_PUBLISH_DRAWING_RETRIES` (default 2, warn when invalid or negative). Add a `DeadlineMargin` constant of 5 minutes and a `CutoffUtc` helper. Keep `Force` unchanged.**
-  **Add `PublishReason.AlreadyPublishedInRun`.**
-  **Update `Plant3dDrawingChangeDetector`. Always query the latest version. When `Force` or `AlwaysPublish` is set, return `AlreadyPublishedInRun` (skip) if the run ID and hash match, otherwise return `Forced`. Normal mode is unchanged. A failed server check returns a result that is classified as `Transient`.**
-  **Add the `FailureKind` enum (`None`, `Transient`, `Fatal`, `Cancelled`) and add the `FailureKind` property to `Plant3dAutomationPublishResult`. Add factory methods for `AlreadyPublishedInRun` and `Cancelled`.**
-  **Create `Plant3dFailureClassifier`. This one class maps exceptions and errors to a `FailureKind`. `TimeoutException`, `HttpRequestException`, HTTP 408/429/5xx and server check failures are `Transient`. HTTP 401/403, conversion errors, no objects, drawing not opened or locked, and invalid mappings are `Fatal`.**
-  **Create `Plant3dConfigException`. Replace the early `return []` config paths in `PublishAsync` with throws.**
-  **Refactor the loop in `PublishAsync`. Check the cut-off before each drawing and mark the remaining drawings `Cancelled` once it has passed. Wrap change detection and the publish in a retry loop that retries only `Transient` failures, up to `DrawingRetries` times. Wait 5s, 15s, then 45s between tries and check the cut-off before each retry. Pass `options.RunId` into the fingerprint in `TagVersionAsync`.**
-  **Create `Plant3dRunResult`, a small record with `RunId`, `Status`, `Force`, `StartedUtc`, `CompletedUtc`, `Counts` and `Failures`. Its writer serializes indented camelCase JSON to `*.result.json.tmp`, then renames the file over `*.result.json`. Evaluate status in this order: `ConfigError`, then `Cancelled`, `Success`, `Partial`, `Failed`.**
-  **Update `FileLoggerProvider.CreateLogFilePath()` to name the log `plant3d-automation-{RunId}.log` and remove the TODO. Add a helper that returns the result path from the same folder.**
-  **Split the commands. Move the shared code (container setup, result logging, summary including `alreadyPublishedInRun` and `cancelled`) into a helper. `SPECKLEPUBLISH` keeps its current behavior. `-SPECKLEPUBLISH` logs `Publish started` (run ID, force, deadline, retries), catches `Plant3dConfigException` and other exceptions, writes the result file in `finally`, closes all documents without saving, then calls `Application.Quit()`.**
-  **Update `Scripts\pub.lsp` to call `-SPECKLEPUBLISH`. Keep `-openproject` and the double-open workaround. Remove the "check for published version message" text. Update `Scripts\pub.scr` so it has no repo-specific path, as decided in step 1.**
-  **Rewrite `Scripts\pub.ps1` to follow the spec flow. Add the `-TimeoutMinutes 120`, `-MaxAttempts 3` and `-RetryDelaySeconds 60` parameters. Build the sanitized run ID. Set the run ID and deadline env vars. For each attempt, delete the result file, start acad with `-PassThru`, wait for it to exit until the deadline, and stop only that PID's process tree on timeout. Read the result file and decide whether to retry. Print a summary. Exit with code 0, 1, 2, 3, 4, 5, 124 or 140 in the spec's priority order.**
-  **Build only the Plant 3D connector project (`dotnet build` on the Plant3d csproj) and fix any errors or analyzer warnings.**
-  **Check every item in `publish-retry-tasks.md` against the spec, tick it off, and add notes on any deviation, such as the static `pub.scr`.**

