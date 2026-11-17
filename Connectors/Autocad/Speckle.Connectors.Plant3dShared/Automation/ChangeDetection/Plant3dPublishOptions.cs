namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Run options for changed-only publishing, read from environment variables.
/// </summary>
public sealed class Plant3dPublishOptions
{
  public const string FORCE_ENV_VAR = "PLANT_PUBLISH_FORCE";

  public Plant3dPublishOptions()
  {
    Force = IsTrue(Environment.GetEnvironmentVariable(FORCE_ENV_VAR));
  }

  /// <summary>When true, every mapped drawing is published regardless of change detection.</summary>
  public bool Force { get; set; }

  private static bool IsTrue(string? value) =>
    value is not null
    && (value.Trim() == "1" || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
}
