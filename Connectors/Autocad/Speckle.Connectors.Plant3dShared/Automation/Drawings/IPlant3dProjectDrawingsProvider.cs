namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// A drawing that belongs to the currently open Plant 3D collaboration project.
/// </summary>
/// <param name="DrawingGuid">The stable GUID Plant 3D assigns to the drawing.</param>
/// <param name="Name">The drawing's display name, as reported by Plant 3D.</param>
/// <param name="FullPath">The absolute path to the drawing file on disk.</param>
public sealed record Plant3dProjectDrawing(Guid DrawingGuid, string Name, string FullPath);

/// <summary>
/// Enumerates all drawings belonging to the current Plant 3D collaboration project (across its project parts).
/// </summary>
public interface IPlant3dProjectDrawingsProvider
{
  IReadOnlyList<Plant3dProjectDrawing> GetProjectDrawings();
}
