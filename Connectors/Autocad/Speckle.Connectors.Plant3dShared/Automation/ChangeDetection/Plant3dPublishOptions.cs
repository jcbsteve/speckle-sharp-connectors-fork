using System.Globalization;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Run options for changed-only publishing, read from environment variables.
/// </summary>
public sealed class Plant3dPublishOptions
{
  public const string FORCE_ENV_VAR = "PLANT_PUBLISH_FORCE";
  public const string RUN_ID_ENV_VAR = "PLANT_PUBLISH_RUN_ID";
  public const string DEADLINE_ENV_VAR = "PLANT_PUBLISH_DEADLINE_UTC";
  public const string DRAWING_RETRIES_ENV_VAR = "PLANT_PUBLISH_DRAWING_RETRIES";
  public const int DEFAULT_DRAWING_RETRIES = 2;

  /// <summary>Time kept before the deadline to finish the current drawing and write the result file.</summary>
  public static readonly TimeSpan DeadlineMargin = TimeSpan.FromMinutes(5);

  private readonly List<string> _warnings = [];

  public Plant3dPublishOptions()
  {
    Force = IsTrue(Environment.GetEnvironmentVariable(FORCE_ENV_VAR));

    var runId = Environment.GetEnvironmentVariable(RUN_ID_ENV_VAR);
    IsBatchRun = !string.IsNullOrWhiteSpace(runId);
    RunId = IsBatchRun ? runId!.Trim() : $"interactive-{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}";

    DeadlineUtc = ReadDeadline();
    DrawingRetries = ReadDrawingRetries();
  }

  /// <summary>When true, every mapped drawing is published once per run regardless of change detection.</summary>
  public bool Force { get; set; }

  /// <summary>False for the batch command, which must fail instead of waiting for user input.</summary>
  public bool AllowPrompts { get; set; } = true;

  /// <summary>Identifies the run in file names and, for batch runs, in the version tag.</summary>
  public string RunId { get; }

  /// <summary>True when the run ID was supplied by the script.</summary>
  public bool IsBatchRun { get; }

  /// <summary>The run ID written to the version tag. Interactive runs leave it out.</summary>
  public string? TagRunId => IsBatchRun ? RunId : null;

  /// <summary>The script's hard deadline, or null when there is none.</summary>
  public DateTimeOffset? DeadlineUtc { get; }

  /// <summary>The time after which no new drawing or retry is started.</summary>
  public DateTimeOffset? CutoffUtc => DeadlineUtc - DeadlineMargin;

  /// <summary>Extra tries per drawing for transient failures.</summary>
  public int DrawingRetries { get; }

  /// <summary>Invalid environment variable values. Logged by the caller, as this class is used by the file logger.</summary>
  public IReadOnlyList<string> Warnings => _warnings;

  /// <summary>True when the cut-off is set and has passed.</summary>
  public bool IsPastCutoff(DateTimeOffset now) => now >= CutoffUtc;

  private DateTimeOffset? ReadDeadline()
  {
    var value = Environment.GetEnvironmentVariable(DEADLINE_ENV_VAR);
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    if (
      DateTimeOffset.TryParse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out var deadline
      )
    )
    {
      return deadline;
    }

    _warnings.Add($"Ignoring invalid {DEADLINE_ENV_VAR} value '{value}'.");
    return null;
  }

  private int ReadDrawingRetries()
  {
    var value = Environment.GetEnvironmentVariable(DRAWING_RETRIES_ENV_VAR);
    if (string.IsNullOrWhiteSpace(value))
    {
      return DEFAULT_DRAWING_RETRIES;
    }

    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var retries))
    {
      return retries;
    }

    _warnings.Add(
      $"Invalid {DRAWING_RETRIES_ENV_VAR} value '{value}'. Using the default of {DEFAULT_DRAWING_RETRIES}."
    );
    return DEFAULT_DRAWING_RETRIES;
  }

  private static bool IsTrue(string? value) =>
    value is not null
    && (value.Trim() == "1" || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
}
