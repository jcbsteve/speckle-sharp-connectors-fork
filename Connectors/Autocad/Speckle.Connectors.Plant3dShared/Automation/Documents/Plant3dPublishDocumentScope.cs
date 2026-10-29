using Autodesk.AutoCAD.ApplicationServices;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Opens (or reuses) a drawing document for the duration of a single publish operation, activates it,
/// and restores the previously active document on disposal. Newly opened documents are closed on disposal;
/// documents that were already open before the scope was created are left open.
/// </summary>
public sealed class Plant3dPublishDocumentScope : IDisposable
{
  private readonly IPlant3dDocumentResolver _documentResolver;
  private readonly Document? _previousDocument;
  private readonly bool _wasAlreadyOpen;
  private bool _disposed;

  public Document Document { get; }

  public Plant3dPublishDocumentScope(IPlant3dDocumentResolver documentResolver, string fullPath)
  {
    _documentResolver = documentResolver;
    _previousDocument = Application.DocumentManager.MdiActiveDocument;

    try
    {
      var openResult = documentResolver.OpenDrawing(fullPath);
      Document = openResult.Document;
      _wasAlreadyOpen = openResult.WasAlreadyOpen;

      documentResolver.SetActiveDocument(Document);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      throw new Plant3dPublishDocumentScopeException($"Failed to open or activate drawing '{fullPath}'.", ex);
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    if (!_wasAlreadyOpen)
    {
      Document.CloseAndDiscard();
    }

    if (_previousDocument is not null)
    {
      _documentResolver.SetActiveDocument(_previousDocument);
    }
  }
}
