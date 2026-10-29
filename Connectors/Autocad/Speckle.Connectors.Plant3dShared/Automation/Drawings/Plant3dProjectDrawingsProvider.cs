using Autodesk.ProcessPower.ProjectManager;
using Microsoft.Extensions.Logging;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Enumerates all drawings belonging to the current Plant 3D collaboration project by walking every
/// project part (PnId, Piping, etc.) reachable from the owning <see cref="PlantProject"/>.
/// </summary>
public sealed class Plant3dProjectDrawingsProvider(ILogger<Plant3dProjectDrawingsProvider> logger)
  : IPlant3dProjectDrawingsProvider
{
  public IReadOnlyList<Plant3dProjectDrawing> GetProjectDrawings()
  {
    var currentProjectPart =
      PnPProjectUtils.GetProjectPartForCurrentDocument()
      ?? throw new InvalidOperationException("No Plant 3D project part could be resolved for the current document.");

    var plantProject =
      currentProjectPart.OwnerPlantProject
      ?? throw new InvalidOperationException("The current project part is not associated with a Plant 3D project.");

    if (plantProject.DocumentService.IsLocalPlantProjectOutOfSync(plantProject))
    {
      throw new InvalidOperationException("The local project is out of sync and must be re-opened or refreshed.");
    }

    List<Plant3dProjectDrawing> drawings = [];
    foreach (Project part in plantProject.ProjectParts)
    {
      foreach (PnPProjectDrawing drawing in part.GetPnPDrawingFiles())
      {
        // TODO investigate file versions to detect changes and only publish if new e.g.
        // var latest = plantProject.DocumentService.GetLatestDocumentVersion(drawing).Document.ModDate;

        var isSame = plantProject.DocumentService.IsLocalFileTheSame(drawing);
        if (!isSame)
        {
          logger.LogInformation("Downloading drawing '{name}'", drawing.Name);
          plantProject.DocumentService.DownloadHeadRevision(part, true, true, SyncHandler, out var _);
        }

        var localPath = Path.Combine(plantProject.ProjectFolderPath, drawing.RelativeFileName);
        if (!Guid.TryParse(drawing.Guid, out var drawingGuid))
        {
          throw new InvalidOperationException($"The drawing '{drawing.Name}' has an invalid GUID: '{drawing.Guid}'.");
        }
        drawings.Add(new Plant3dProjectDrawing(drawingGuid, drawing.Name, localPath));
      }
    }

    return drawings;
  }

  // Need to monitor this and understand how to handle any conflicts that arise
  private void SyncHandler(SyncType type, SyncConflictsResolution _) =>
    System.Diagnostics.Debug.WriteLine($"SyncHandler called for {type}");
}
