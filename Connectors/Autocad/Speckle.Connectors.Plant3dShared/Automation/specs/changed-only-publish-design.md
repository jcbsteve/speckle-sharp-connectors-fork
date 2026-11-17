# Changed-Only Publish: Design

Implements `changed-only-publish-requirements.md`.

## Components (`Automation/ChangeDetection`)

| Component | Responsibility | Requirements |
|---|---|---|
| `Plant3dPublishOptions` | `Force` from `PLANT_PUBLISH_FORCE`. | FR-3, FR-6 |
| `Plant3dPublishSchema` | `VERSION` constant. | FR-7 |
| `DrawingFingerprinter` | Read-only streamed SHA-256, uppercase hex. | FR-1 |
| `VersionFingerprint` | Formats and parses the version message tag. | FR-2, FR-4 |
| `IPlant3dModelVersionService` / `Plant3dModelVersionService` | Latest version id and message; update version message via the SDK. | FR-2, FR-4 |
| `ChangeDecision` / `PublishReason` | Decision result. | FR-3 |
| `Plant3dDrawingChangeDetector` | Runs the decision table. | FR-3 |
| `Plant3dAutomationService` | Skips, publishes, tags versions, reports results. | FR-3, FR-4, FR-5 |

## Flow

```
for each mapped drawing:
  decision = detector.Evaluate(drawing)
  if Unchanged            -> result Unchanged
  if ServerCheckFailed    -> error result, drawing not opened
  else                    -> publish; on success TagVersion(decision.ContentHash)
```

Detector: file missing -> FileUnavailable; hash file; force -> Forced; get latest version (exception -> ServerCheckFailed); parse tag; compare schema then hash.

## Failure handling
- Server query failure: error result, no publish attempt.
- Tag update failure: warning only.
