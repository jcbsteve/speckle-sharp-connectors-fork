namespace Speckle.Connectors.Plant3dShared.Automation;

public enum MappingIssueSeverity
{
  Warning,
  Error,
}

public enum MappingIssueKind
{
  MultipleAccountIds,
  EmptyAccountId,
  MultipleServerUrls,
  EmptyServerUrl,
  DuplicateDrawingGuid,
  SharedTargetModel,
  EmptyDrawingGuid,
  EmptyProjectOrModelId,
  MappingWithoutDrawing,
  DrawingWithoutMapping,
  DuplicateProjectDrawingGuid,
}

/// <summary>
/// A problem found when validating the mappings against the project.
/// </summary>
/// <param name="Mapping">The offending mapping, or null when the issue spans several mappings or has no mapping.</param>
public sealed record MappingIssue(
  MappingIssueSeverity Severity,
  MappingIssueKind Kind,
  DrawingModelMapping? Mapping,
  string Message
)
{
  /// <summary>The drawing the issue refers to, when it refers to a single drawing.</summary>
  public Guid? DrawingGuid { get; init; } = Mapping?.DrawingGuid;

  /// <summary>The mapping's relative path or the project drawing's full path, when known.</summary>
  public string? DrawingPath { get; init; } = Mapping?.RelativePath;
}

/// <summary>
/// Validates drawing model mappings against the current project, collecting every issue so the whole mappings
/// file can be fixed in one pass.
/// </summary>
public static class Plant3dMappingValidator
{
  public static IReadOnlyList<MappingIssue> Validate(
    IReadOnlyList<DrawingModelMapping> mappings,
    IReadOnlyList<Plant3dProjectDrawing> projectDrawings
  )
  {
    var projectGuids = projectDrawings.Select(d => d.DrawingGuid).ToHashSet();

    return
    [
      .. CheckMultipleAccountIds(mappings),
      .. CheckEmptyAccountIds(mappings),
      .. CheckMultipleServerUrls(mappings),
      .. CheckEmptyServerUrls(mappings),
      .. CheckDuplicateDrawingGuids(mappings),
      .. CheckSharedTargetModels(mappings),
      .. CheckEmptyDrawingGuids(mappings),
      .. CheckMappingsWithoutDrawing(mappings, projectGuids),
      .. CheckDrawingsWithoutMapping(mappings, projectDrawings),
      .. CheckEmptyProjectOrModelIds(mappings),
      .. CheckDuplicateProjectDrawingGuids(mappings, projectDrawings),
    ];
  }

  private static IEnumerable<MappingIssue> CheckMultipleAccountIds(IReadOnlyList<DrawingModelMapping> mappings)
  {
    var accountIds = mappings.Select(m => m.AccountId).Distinct().ToList();
    if (accountIds.Count > 1)
    {
      yield return new(
        MappingIssueSeverity.Error,
        MappingIssueKind.MultipleAccountIds,
        null,
        $"Mappings contain multiple account IDs: {string.Join(", ", accountIds)}."
      );
    }
  }

  private static IEnumerable<MappingIssue> CheckEmptyAccountIds(IReadOnlyList<DrawingModelMapping> mappings)
  {
    foreach (var mapping in mappings.Where(m => string.IsNullOrWhiteSpace(m.AccountId)))
    {
      yield return new(
        MappingIssueSeverity.Error,
        MappingIssueKind.EmptyAccountId,
        mapping,
        $"Mapping for drawing '{mapping.DrawingGuid}' has an empty account ID."
      );
    }
  }

  private static IEnumerable<MappingIssue> CheckMultipleServerUrls(IReadOnlyList<DrawingModelMapping> mappings)
  {
    var serverUrls = mappings.Select(m => m.ServerUrl).Distinct().ToList();
    if (serverUrls.Count > 1)
    {
      yield return new(
        MappingIssueSeverity.Error,
        MappingIssueKind.MultipleServerUrls,
        null,
        $"Mappings contain multiple server URLs: {string.Join(", ", serverUrls)}."
      );
    }
  }

  private static IEnumerable<MappingIssue> CheckEmptyServerUrls(IReadOnlyList<DrawingModelMapping> mappings)
  {
    foreach (var mapping in mappings.Where(m => string.IsNullOrWhiteSpace(m.ServerUrl?.OriginalString)))
    {
      yield return new(
        MappingIssueSeverity.Error,
        MappingIssueKind.EmptyServerUrl,
        mapping,
        $"Mapping for drawing '{mapping.DrawingGuid}' has an empty server URL."
      );
    }
  }

  private static IEnumerable<MappingIssue> CheckDuplicateDrawingGuids(IReadOnlyList<DrawingModelMapping> mappings)
  {
    // Empty GUIDs are reported by CheckEmptyDrawingGuids and are not reported again here.
    var duplicates = mappings
      .Where(m => m.DrawingGuid != Guid.Empty)
      .GroupBy(m => m.DrawingGuid)
      .Where(g => g.Count() > 1);
    foreach (var duplicate in duplicates)
    {
      yield return new(
        MappingIssueSeverity.Error,
        MappingIssueKind.DuplicateDrawingGuid,
        duplicate.First(),
        $"Drawing GUID '{duplicate.Key}' is mapped {duplicate.Count()} times."
      );
    }
  }

  // Drawings sharing a model overwrite each other's latest version, so change detection would always republish.
  private static IEnumerable<MappingIssue> CheckSharedTargetModels(IReadOnlyList<DrawingModelMapping> mappings)
  {
    // Empty IDs are reported by CheckEmptyProjectOrModelIds and are not reported again here.
    var sharedTargets = mappings
      .Where(m => !string.IsNullOrWhiteSpace(m.ProjectId) && !string.IsNullOrWhiteSpace(m.ModelId))
      .GroupBy(m => (m.ProjectId, m.ModelId))
      .Where(g => g.Count() > 1);
    foreach (var target in sharedTargets)
    {
      yield return new(
        MappingIssueSeverity.Error,
        MappingIssueKind.SharedTargetModel,
        null,
        $"Model '{target.Key.ProjectId}/{target.Key.ModelId}' is targeted by multiple drawings: "
          + string.Join(", ", target.Select(m => m.DrawingGuid))
          + "."
      );
    }
  }

  private static IEnumerable<MappingIssue> CheckEmptyDrawingGuids(IReadOnlyList<DrawingModelMapping> mappings)
  {
    foreach (var mapping in mappings.Where(m => m.DrawingGuid == Guid.Empty))
    {
      yield return new(
        MappingIssueSeverity.Error,
        MappingIssueKind.EmptyDrawingGuid,
        mapping,
        $"Mapping for '{mapping.RelativePath}' has an empty drawing GUID."
      );
    }
  }

  private static IEnumerable<MappingIssue> CheckMappingsWithoutDrawing(
    IReadOnlyList<DrawingModelMapping> mappings,
    HashSet<Guid> projectGuids
  )
  {
    // Empty GUIDs are reported by CheckEmptyDrawingGuids and are not reported again here.
    foreach (var mapping in mappings.Where(m => m.DrawingGuid != Guid.Empty && !projectGuids.Contains(m.DrawingGuid)))
    {
      yield return new(
        MappingIssueSeverity.Warning,
        MappingIssueKind.MappingWithoutDrawing,
        mapping,
        $"Mapped drawing '{mapping.DrawingGuid}' ('{mapping.RelativePath}') not found in the current project."
      );
    }
  }

  private static IEnumerable<MappingIssue> CheckDrawingsWithoutMapping(
    IReadOnlyList<DrawingModelMapping> mappings,
    IReadOnlyList<Plant3dProjectDrawing> projectDrawings
  )
  {
    var mappedGuids = mappings.Select(m => m.DrawingGuid).ToHashSet();
    foreach (var drawing in projectDrawings.Where(d => !mappedGuids.Contains(d.DrawingGuid)))
    {
      yield return new(
        MappingIssueSeverity.Warning,
        MappingIssueKind.DrawingWithoutMapping,
        null,
        $"Project drawing '{drawing.Name}' ({drawing.DrawingGuid}) has no mapping."
      )
      {
        DrawingGuid = drawing.DrawingGuid,
        DrawingPath = drawing.FullPath,
      };
    }
  }

  // Mapped drawings sharing a GUID would publish to the same model and overwrite each other's latest version.
  private static IEnumerable<MappingIssue> CheckDuplicateProjectDrawingGuids(
    IReadOnlyList<DrawingModelMapping> mappings,
    IReadOnlyList<Plant3dProjectDrawing> projectDrawings
  )
  {
    var duplicates = projectDrawings.GroupBy(d => d.DrawingGuid).Where(g => g.Count() > 1);
    foreach (var duplicate in duplicates)
    {
      var mapping = mappings.FirstOrDefault(m => m.DrawingGuid == duplicate.Key);
      yield return new(
        mapping is null ? MappingIssueSeverity.Warning : MappingIssueSeverity.Error,
        MappingIssueKind.DuplicateProjectDrawingGuid,
        mapping,
        $"Drawing GUID '{duplicate.Key}' is shared by {duplicate.Count()} project drawings: "
          + string.Join(", ", duplicate.Select(d => d.FullPath))
          + "."
      );
    }
  }

  private static IEnumerable<MappingIssue> CheckEmptyProjectOrModelIds(IReadOnlyList<DrawingModelMapping> mappings)
  {
    foreach (
      var mapping in mappings.Where(m => string.IsNullOrWhiteSpace(m.ProjectId) || string.IsNullOrWhiteSpace(m.ModelId))
    )
    {
      yield return new(
        MappingIssueSeverity.Error,
        MappingIssueKind.EmptyProjectOrModelId,
        mapping,
        $"Mapping for drawing '{mapping.DrawingGuid}' has an empty ProjectId or ModelId."
      );
    }
  }
}
