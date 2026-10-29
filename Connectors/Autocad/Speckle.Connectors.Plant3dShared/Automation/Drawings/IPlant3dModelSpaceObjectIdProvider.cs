namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Retrieves the object IDs of all entities in a document's model space.
/// </summary>
public interface IPlant3dModelSpaceObjectIdProvider
{
  List<string> GetAllModelSpaceObjectIds(Document document);
}
