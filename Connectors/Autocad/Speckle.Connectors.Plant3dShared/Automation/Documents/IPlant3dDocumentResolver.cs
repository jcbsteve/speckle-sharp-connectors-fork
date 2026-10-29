namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Resolves and activates the AutoCAD/Plant3D <see cref="Document"/> to be published.
/// </summary>
public interface IPlant3dDocumentResolver
{
  /// <summary>
  /// Returns the already-open document matching <paramref name="fullPath"/>, or opens it if it isn't loaded.
  /// </summary>
  DocumentOpenResult OpenDrawing(string fullPath);

  void SetActiveDocument(Document document);
}

/// <summary>
/// The outcome of resolving a drawing document, indicating whether it was already open
/// prior to the resolution attempt (and therefore should not be closed by the caller).
/// </summary>
public readonly record struct DocumentOpenResult(Document Document, bool WasAlreadyOpen);
