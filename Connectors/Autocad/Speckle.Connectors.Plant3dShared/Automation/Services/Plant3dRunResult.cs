using System.Text.Json;
using System.Text.Json.Serialization;

namespace Speckle.Connectors.Plant3dShared.Automation;

public enum Plant3dRunStatus
{
  Success,
  Partial,
  Failed,
  Cancelled,
  ConfigError,
}

public sealed record Plant3dRunCounts(
  int Published,
  int Unchanged,
  int AlreadyPublishedInRun,
  int Failed,
  int Cancelled,
  int MappingsWithoutDrawing,
  int DrawingsWithoutMapping
)
{
  /// <summary>Counts the drawing results. The single definition used by the result file and the log summary.</summary>
  public static Plant3dRunCounts From(IReadOnlyList<Plant3dAutomationPublishResult> results) =>
    new(
      results.Count(r => r.Executed && r.Succeeded),
      results.Count(r => r.IsUnchanged),
      results.Count(r => r.IsAlreadyPublishedInRun),
      results.Count(r => r.FailureKind is FailureKind.Transient or FailureKind.Fatal),
      results.Count(r => r.FailureKind == FailureKind.Cancelled),
      results.Count(r => r.Reason == PublishReason.MappingWithoutDrawing),
      results.Count(r => r.Reason == PublishReason.DrawingWithoutMapping)
    );
}

public sealed record Plant3dRunFailure(
  [property: JsonPropertyName("guid")] Guid DrawingGuid,
  string Path,
  FailureKind FailureKind,
  string? Error
);

/// <summary>
/// The result file read by <c>pub.ps1</c> after Plant 3D exits. Written once per attempt.
/// </summary>
public sealed record Plant3dRunResult(
  string RunId,
  Plant3dRunStatus Status,
  bool Force,
  DateTimeOffset StartedUtc,
  DateTimeOffset CompletedUtc,
  Plant3dRunCounts Counts,
  IReadOnlyList<Plant3dRunFailure> Failures,
  string? Error = null
)
{
  internal static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() },
  };

  /// <summary>Builds the run result from the drawing results, or a config error when nothing was attempted.</summary>
  public static Plant3dRunResult Create(
    Plant3dPublishOptions options,
    DateTimeOffset startedUtc,
    IReadOnlyList<Plant3dAutomationPublishResult> results,
    string? configError
  )
  {
    var counts = Plant3dRunCounts.From(results);

    var failures = results
      .Where(r => !r.Succeeded)
      .Select(r => new Plant3dRunFailure(r.DrawingGuid, r.DrawingPath, r.FailureKind, r.ErrorMessage))
      .ToList();

    var succeeded = results.Count - failures.Count;
    var status =
      configError is not null ? Plant3dRunStatus.ConfigError
      : counts.Cancelled > 0 ? Plant3dRunStatus.Cancelled
      : failures.Count == 0 ? Plant3dRunStatus.Success
      : succeeded > 0 ? Plant3dRunStatus.Partial
      : Plant3dRunStatus.Failed;

    return new Plant3dRunResult(
      options.RunId,
      status,
      options.Force,
      startedUtc,
      DateTimeOffset.UtcNow,
      counts,
      failures,
      configError
    );
  }

  /// <summary>Writes to a temporary file and renames it, so the script never reads a half-written file.</summary>
  public void Write(string path) => WriteJson(path, this);

  /// <summary>Writes to a temporary file and renames it, so the script never reads a half-written file.</summary>
  internal static void WriteJson<T>(string path, T value)
  {
    var tempPath = path + ".tmp";
    File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
    File.Move(tempPath, path, overwrite: true);
  }
}

/// <summary>
/// The live drawing inventory of the open Plant 3D project, read by the drawing-to-model mapper.
/// </summary>
public sealed record Plant3dDrawingInventory(
  int Schema,
  string ProjectName,
  DateTimeOffset GeneratedUtc,
  IReadOnlyList<Plant3dProjectDrawing> Drawings
)
{
  /// <summary>Writes the inventory of the given drawings to the run's inventory file and returns its path.</summary>
  public static string Write(string runId, string projectName, IReadOnlyList<Plant3dProjectDrawing> drawings)
  {
    var path = Logging.FileLoggerProvider.GetInventoryFilePath(runId);
    Plant3dRunResult.WriteJson(path, new Plant3dDrawingInventory(1, projectName, DateTimeOffset.UtcNow, drawings));
    return path;
  }
}
