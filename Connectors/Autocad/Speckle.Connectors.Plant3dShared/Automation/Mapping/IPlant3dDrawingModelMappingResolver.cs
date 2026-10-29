namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Resolves the drawing-to-model mappings file path (env var or user prompt) and deserializes it.
/// </summary>
public interface IPlant3dDrawingModelMappingResolver
{
  string? GetMappingsPath();
  IReadOnlyList<DrawingModelMapping> DeserializeMappings(string mappingsPath);
}
