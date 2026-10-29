using Autodesk.AutoCAD.ApplicationServices;
using Microsoft.Extensions.Logging;

namespace Speckle.Connectors.Plant3dShared.Automation;

public sealed class Plant3dDocumentResolver(ILogger<Plant3dDocumentResolver> logger) : IPlant3dDocumentResolver
{
  public DocumentOpenResult OpenDrawing(string fullPath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

    foreach (Document doc in Application.DocumentManager)
    {
      if (string.Equals(doc.Name, fullPath, StringComparison.OrdinalIgnoreCase))
      {
        return new DocumentOpenResult(doc, WasAlreadyOpen: true);
      }
    }

    var opened = Application.DocumentManager.Open(fullPath, true);
    return new DocumentOpenResult(opened, WasAlreadyOpen: false);
  }

  public void SetActiveDocument(Document document)
  {
    ArgumentNullException.ThrowIfNull(document);

    if (!IsActiveDocument(document))
    {
      logger.LogInformation("Switching to document '{name'}", document.Name);
      Application.DocumentManager.MdiActiveDocument = document;
      Application.DocumentManager.CurrentDocument = document;
    }
  }

  private static bool IsActiveDocument(Document document) =>
    ReferenceEquals(document, Application.DocumentManager.MdiActiveDocument)
    && ReferenceEquals(document, Application.DocumentManager.CurrentDocument);
}
