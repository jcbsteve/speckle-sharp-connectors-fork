using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;

namespace Speckle.Connectors.Plant3dShared.Automation;

public sealed class Plant3dModelSpaceObjectIdProvider : IPlant3dModelSpaceObjectIdProvider
{
  public List<string> GetAllModelSpaceObjectIds(Document document)
  {
    ArgumentNullException.ThrowIfNull(document);

    using var transaction = document.Database.TransactionManager.StartTransaction();
    var modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(document.Database);
    var modelSpace = (BlockTableRecord)transaction.GetObject(modelSpaceId, OpenMode.ForRead);
    var objectIds = modelSpace
      .Cast<ObjectId>()
      .Where(objectId => !objectId.IsNull)
      .Select(objectId => objectId.Handle.Value.ToString(CultureInfo.InvariantCulture))
      .ToList();
    transaction.Commit();

    return objectIds;
  }

  public List<string> GetAllTopLevelSpaceObjectIds(Document document)
  {
    ArgumentNullException.ThrowIfNull(document);

    using var transaction = document.Database.TransactionManager.StartTransaction();

    var blockTable = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead);

    var objectIds = new List<string>();

    foreach (ObjectId btrId in blockTable)
    {
      var btr = (BlockTableRecord)transaction.GetObject(btrId, OpenMode.ForRead);

      if (!btr.IsLayout)
      {
        continue;
      }

      objectIds.AddRange(
        btr.Cast<ObjectId>()
          .Where(id => !id.IsNull)
          .Select(id => id.Handle.Value.ToString(CultureInfo.InvariantCulture))
      );
    }

    transaction.Commit();

    return objectIds;
  }
}
