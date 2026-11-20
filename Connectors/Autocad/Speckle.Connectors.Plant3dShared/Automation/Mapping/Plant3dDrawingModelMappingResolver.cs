using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Resolves the drawing-to-model mappings file path from the <c>PLANT_DRAWING_MAPPINGS</c> environment
/// variable (falling back to a user prompt/default location) and deserializes the mapping list.
/// </summary>
public sealed class Plant3dDrawingModelMappingResolver(
  IJsonSerializer jsonSerializer,
  IPlant3dEditor editor,
  Plant3dPublishOptions options,
  ILogger<Plant3dDrawingModelMappingResolver> logger
) : IPlant3dDrawingModelMappingResolver
{
  private const string MAPPINGS_ENV_VAR = "PLANT_DRAWING_MAPPINGS";

  private readonly string _defaultMappingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Speckle",
    "Connectors",
    "Plant3D",
    "DrawingModelMappings.json"
  );

  public string? GetMappingsPath()
  {
    var envMappingsPath = Environment.GetEnvironmentVariable(MAPPINGS_ENV_VAR);
    var hasEnvPath = !string.IsNullOrWhiteSpace(envMappingsPath);
    if (!options.AllowPrompts)
    {
      return hasEnvPath
        ? envMappingsPath
        : throw new Plant3dConfigException($"The {MAPPINGS_ENV_VAR} environment variable is not set.");
    }

    // Interactive runs always confirm the path; the env var, when set, is offered as the default.
    var defaultPath = hasEnvPath ? envMappingsPath! : _defaultMappingsPath;
    if (!hasEnvPath)
    {
      logger.LogWarning(
        "No drawing model mappings file specified in the {MAPPINGS_ENV_VAR} environment variable.",
        MAPPINGS_ENV_VAR
      );
    }
    logger.LogInformation("Default mappings path is {DefaultMappingsPath}.", defaultPath);

    var useDefault = editor.GetKeywords("Use default drawing model mappings path [Yes/No]", ["Yes", "No"]);
    return useDefault switch
    {
      "Yes" => defaultPath,
      "No" => AskForMappingsPath(),
      _ => null,
    };
  }

  private string? AskForMappingsPath()
  {
    var mappingsPath = editor.GetString("Enter drawing model mappings path:");
    return string.IsNullOrWhiteSpace(mappingsPath) ? null : mappingsPath;
  }

  public IReadOnlyList<DrawingModelMapping> DeserializeMappings(string mappingsPath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(mappingsPath);

    return jsonSerializer.Deserialize<List<DrawingModelMapping>>(File.ReadAllText(mappingsPath))
      ?? throw new InvalidDataException($"The drawing model mappings file '{mappingsPath}' is empty or invalid.");
  }
}
