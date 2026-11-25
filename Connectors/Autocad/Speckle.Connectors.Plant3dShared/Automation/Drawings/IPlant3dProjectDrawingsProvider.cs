namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// A drawing that belongs to the currently open Plant 3D collaboration project.
/// </summary>
/// <param name="DrawingGuid">The stable GUID Plant 3D assigns to the drawing.</param>
/// <param name="Name">The drawing's display name, as reported by Plant 3D.</param>
/// <param name="FullPath">The absolute path to the drawing file on disk.</param>
/// <param name="RelativePath">The drawing path relative to the project folder.</param>
/// <param name="Part">The project part (P&amp;ID, Piping, etc.) the drawing belongs to.</param>
public sealed record Plant3dProjectDrawing(
  Guid DrawingGuid,
  string Name,
  [property: System.Text.Json.Serialization.JsonIgnore] string FullPath,
  string RelativePath,
  string Part
);

/// <summary>
/// Enumerates all drawings belonging to the current Plant 3D collaboration project (across its project parts).
/// </summary>
public interface IPlant3dProjectDrawingsProvider
{
  /// <summary>Gets the project drawings, downloading stale ones first when <paramref name="syncOutOfDate"/> is set.</summary>
  IReadOnlyList<Plant3dProjectDrawing> GetProjectDrawings(bool syncOutOfDate = true);

  /// <summary>Gets the name of the open Plant 3D project.</summary>
  string GetProjectName();
}
