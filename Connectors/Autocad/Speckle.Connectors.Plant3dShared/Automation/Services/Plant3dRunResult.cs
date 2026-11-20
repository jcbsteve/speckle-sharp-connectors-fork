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

public sealed record Plant3dRunCounts(int Published, int Unchanged, int AlreadyPublishedInRun, int Failed, int Cancelled);

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
  private static readonly JsonSerializerOptions s_jsonOptions = new()
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
    var counts = new Plant3dRunCounts(
      results.Count(r => r.Executed && r.Succeeded),
      results.Count(r => r.IsUnchanged),
      results.Count(r => r.IsAlreadyPublishedInRun),
      results.Count(r => r.FailureKind is FailureKind.Transient or FailureKind.Fatal),
      results.Count(r => r.FailureKind == FailureKind.Cancelled)
    );

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
  public void Write(string path)
  {
    var tempPath = path + ".tmp";
    File.WriteAllText(tempPath, JsonSerializer.Serialize(this, s_jsonOptions));
    File.Move(tempPath, path, overwrite: true);
  }
}
