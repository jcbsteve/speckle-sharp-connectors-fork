# Changed-Only Publish: Requirements

## Goal
Unchanged drawings are never opened in Plant 3D; only the file is read, read-only, to compute its hash.

## Fingerprint
Stored on the server in the message of the published version:

`[speckle-plant3d sha256=<64 hex> schema=<N>]`

| Field | Meaning |
|---|---|
taken before opening it in Plant 3D.
| schema | `Plant3dPublishSchema.VERSION` at publish time. |

## Functional requirements
Before opening a drawing in Plant 3D, hash the file
- **FR-2** Fetch the mapped model's latest version and parse the fingerprint from its message.
- **FR-3** Decision, first match wins:

  | # | Condition | Result | Reason |
  |---|---|---|---|
  | 1 | File missing or unreadable | Publish | `FileUnavailable` |
  | 2 | `PLANT_PUBLISH_FORCE` or mapping `alwaysPublish` | Publish | `Forced` |
  | 3 | Server check failed | Skip, error result | `ServerCheckFailed` |
  | 4 | No version or no fingerprint | Publish | `NeverPublished` |
  | 5 | Schema differs | Publish | `SchemaChanged` |
  | 6 | Hash differs | Publish | `ContentChanged` |
  | 7 | Otherwise | Skip | `Unchanged` |

- **FR-4** After a successful publish, set the new version's message to include the fingerprint.
- **FR-5** If tagging fails, log a warning; the publish still succeeds and the drawing is republished next run.
- **FR-6** No local state files exist. The only environment variable is `PLANT_PUBLISH_FORCE`.
- **FR-7** Incrementing `Plant3dPublishSchema.VERSION` republishes every drawing once.

## Out of scope
Xrefs, drawing save/check-in/check-out, conversion-setting hashing, drawing GUID in the fingerprint.

## Known consequences
- Every mapped drawing is hashed each run.
- Remapping a drawing to a model without a fingerprint causes one publish.
- Manual edits to the version message that remove the tag cause one republish.
