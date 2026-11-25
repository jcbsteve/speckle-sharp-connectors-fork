# Plant 3D drawing → Speckle model mapping: improvement options

## 1. Current state and why it lags

```
[Report Creator] --TSV--> [SpeckleModelSync] --DrawingModelMappings.json--> [pub.ps1 → Plant 3D -SPECKLEPUBLISH]
   reads local cache        name-match + create          materialised GUID→model list          opens project (refreshes cache),
   (stale SQLite)           models in one project                                              enumerates live drawings
```

- `PnPReportCreator.exe /PROJECT <CollaborationCache>\Project.xml` reads the **local SQLite cache**. Autodesk
  documents that the cache only refreshes on *Open Collaboration Project*, *Refresh from Project* or checkout.
  So the report reflects the last time a Plant 3D session opened the project on that machine. In practice that is
  usually the *previous* overnight publish.
- The publish run **already has the authoritative list**: `pub.lsp` opens the project (refreshing the cache) and
  `Plant3dProjectDrawingsProvider` enumerates `ProjectParts → GetPnPDrawingFiles()` live, with GUID, name and path.
- Effect: new drawings appear as `DrawingWithoutMapping` and are skipped for one or more cycles. The validator and
  result file already count them (`counts.drawingsWithoutMapping`).

### Other weaknesses in the current mapper (SpeckleModelSync)

| Issue | Consequence |
|---|---|
| Re-matches **by name** each time, not sticky by GUID | Moving or renaming a drawing in Plant 3D creates a new model. The old model is orphaned and version history is broken. |
| `Workspace.GetProjects(...)` only reads the first page (`// paging...`) | Models in later projects are not seen, so duplicates can be created in larger workspaces. |
| "First match wins" across projects | Duplicate model names across projects resolve silently and non-deterministically. |
| Routing logic (normalisation regex, single `SPECKLE_PROJECT_ID`) is in C# | Changing the matching convention needs a code change and rebuild. |
| Report Creator dependency | Needs `DrawingListExport.rcfx` installed under ProgramData per machine and version, plus fixed-width/tab TSV parsing. |
| Account, server and workspace repeated on every entry | The file is noisy (about 3,200 lines). The validator already enforces a single account and server. |

## 2. Goals (from the brief)

1. Reflect current collab drawings (no lag).
2. Simple to configure and run as one scheduled job.
3. Easy to maintain and modify.
4. Keep the mapping **isolated and flexible**, because the drawing → model convention is still volatile.

## 3. Options

### Option A: Refresh the cache before Report Creator (minimal change)
Add a stage 0 that launches Plant 3D, runs `-openproject` and then quits. After that, run Report Creator, the mapper, and the publish.

- ✅ Small change. Existing tools are unchanged.
- ❌ Two Plant 3D launches per night. Report Creator and the rcfx template remain. The TSV and the publish
  enumeration could still disagree, because they use different APIs.
- **Score:** goal 1 ✅, goal 2 ⚠️, goal 3 ❌, goal 4 ✅

### Option B: Plant 3D emits the drawing inventory (replaces Report Creator)
Reuse `IPlant3dProjectDrawingsProvider` to write `drawings.json` (GUID, name, relative path, project part, and
optionally the local file timestamp) from inside Plant 3D. There are two ways to emit it:
- **B1.** A new batch command, `-SPECKLELIST`. It opens the project, writes the inventory, and quits. It does not download drawings.
- **B2.** `-SPECKLEPUBLISH` always writes `<runId>.drawings.json` next to its result file.

The mapper reads JSON instead of the TSV.

- ✅ Same API and semantics as the publish run, so GUIDs and paths always match. No rcfx template.
- ✅ Adds the project part type (P&ID, Piping, Ortho, Iso). This is useful routing data that the report does not provide today.
- B1 has no lag but needs an extra launch. B2 has a one-cycle lag on its own (see Option C for how to remove it).
- **Score:** goal 1 ✅ (B1), goal 2 ✅, goal 3 ✅, goal 4 ✅. Mapping stays a separate tool.

### Option C: Self-healing orchestration (B2 plus conditional re-run) ⭐
`pub.ps1` (or a thin `run-nightly.ps1`) does the following:
1. Publish using the current mapping file. The run writes the live inventory.
2. If `result.counts.drawingsWithoutMapping > 0`, run the mapper against **that run's inventory**.
3. Publish again. Changed-only publishing makes the re-run cheap, because everything already published is `Unchanged`. Only newly mapped drawings are sent.

- ✅ No lag. A second Plant 3D launch happens **only** on nights with new drawings.
- ✅ Reuses the existing result-file and retry plumbing. One scheduled task with one set of settings.
- ✅ The mapper stays an independent executable, so it is isolated and swappable.
- ⚠️ The mapper needs model-create permission, which it already has today.

### Option D: Mapping hook inside the publish run (out of process)
The connector writes the inventory and invokes a configured command, for example
`PLANT_MAPPING_COMMAND="SpeckleModelSync.exe --inventory {in} --out {out}"`. It then loads the output before
publishing. If the hook fails or times out, the connector falls back to the last good mapping file.

- ✅ Single launch, no lag, and the mapper remains isolated.
- ❌ Process orchestration inside acad.exe (timeouts, exit codes, credentials for the child process). This adds complexity to the
  connector for little gain over Option C.

### Option E: Declarative rules resolved inside the connector
Replace the materialised list with a small rules file. The connector maps and creates models at publish time:

```jsonc
{
  "serverUrl": "https://design.jacobs.com",
  "workspaceSlug": "lifesciences",
  "defaults": { "projectId": "e5c0ac96b5", "modelName": "{dir}/{fileNameNoExt}" },
  "rules": [
    { "match": "PID DWG/**",     "projectId": "<pid-project>" },
    { "match": "**/Archive/**",  "exclude": true }
  ],
  "pins": [ { "drawingGuid": "…", "projectId": "…", "modelId": "…" } ]
}
```

- ✅ One small, human-editable file. No separate tool. Always live.
- ❌ The volatile matching logic moves into the connector release cycle, which **reduces isolation** (goal 4). The
  publish token needs model-create scope. Renames still need "sticky" state, which pulls the lockfile concept back in.
- Good long-term target **once the convention stabilises**.

### Option F: Speckle as the mapping store
Tag each model with its drawing GUID (the provisioner already writes `Automatically created for drawing GUID …` in
the description). At publish time, list the models in the configured projects and resolve by GUID.

- ✅ No mapping file at all.
- ❌ Descriptions are user-editable and there is no server-side query by description. Overrides and exclusions have nowhere to go.
  Useful as a **fallback or repair** signal, not as the primary mechanism.

## 4. Recommendation

Adopt **B + C** now. Shape the mapper so it can evolve into **E** later.

1. **Inventory from Plant 3D (B1 + B2).** Add a JSON inventory writer that uses `IPlant3dProjectDrawingsProvider`
   without downloads. Expose it through `-SPECKLELIST`, and have `-SPECKLEPUBLISH` emit it on every run. Keep the TSV loader in
   the mapper for transition, then retire Report Creator.
2. **Make the mapper incremental and sticky.** Use these inputs:
   - the inventory
   - the previous mapping file, which acts as the lockfile
   - a small `mapping-rules.json` that holds the volatile routing (target project per folder or part type, model-name template, excludes, pins)

   Behaviour:
   - Existing GUID → model entries are kept. Renames and moves do not create new models.
   - Only new GUIDs are matched or created.
   - Orphans are reported, not deleted.
   - Workspace projects are paged correctly.
   - Ambiguous name matches are reported instead of "first wins".
   - The file is written atomically.
3. **Single scheduled entry point (C).** `pub.ps1` gets a `-MappingCommand` setting, plus `-RemapOnDrift` (default on).
   The flow is: publish, then remap if any drawings had no mapping, then publish again. The scheduled task calls only this
   script.
4. **Optional schema tidy-up.** Move `ServerUrl`, `AccountId` and `Workspace*` to a top-level header in the mapping
   file. Keep per-entry overrides possible. Make the connector deserializer accept both shapes.

Why: it removes the lag and the Report Creator dependency, gives one scheduled job, and keeps the volatile matching
in a separate, independently deployable tool configured by data (the rules file) rather than code. The connector only learns to
**emit** an inventory, which is a stable contract that is useful regardless of how mapping evolves.

### Suggested inventory contract (`drawings.json`)
```json
{
  "schema": 1,
  "projectName": "Plant3D_79232600_Pfizer_DS2_Detailed-Design",
  "generatedUtc": "2026-11-25T02:10:00Z",
  "drawings": [
    { "drawingGuid": "57cddf70-…", "name": "20-JAC-PQ-10-L-001.dwg",
      "relativePath": "Plant 3D Models\\Equipment\\20-JAC-PQ-10-L-001.dwg", "part": "Piping" }
  ]
}
```

### Side note found during review (verified by decompiling PnPCloudService.dll, Plant 3D 2026)
`DownloadHeadRevision(part, …)` is guarded per drawing by `IsLocalFileTheSame`.

What a part download does:
- It refreshes **every** stale file in the part. `bFoldersOnly` is ignored.
- It stamps each downloaded file with the server's `ModifiedAt`.
- It updates the in-memory server item cache.

As a result, later checks for drawings in the same part return true, and the part is not downloaded again. The
exception is a drawing that the automation account has checked out: it is never overwritten, so it triggers a part
sync on every check.

`IsLocalPlantProjectOutOfSync` runs first and throws if any file in its diff is stale. That makes the per-drawing
download a fallback that is rarely reached. An inventory-only path should skip downloads entirely.
