using Speckle.Connectors.Autocad.Filters;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;

namespace Speckle.Connectors.Plant3dShared.Automation;

/// <summary>
/// Creates or updates a sender model card for a Plant 3D drawing being published.
/// </summary>
public interface IModelCardWriter
{
  /// <summary>
  /// Creates a new sender model card for <paramref name="drawing"/> with the given <paramref name="objectIds"/>
  /// as its selection filter, and adds or updates it in the model store.
  /// </summary>
  /// <returns>The model card id.</returns>
  string Upsert(DrawingToPublish drawing, List<string> objectIds);
}

public sealed class ModelCardWriter(IDocumentModelStore store) : IModelCardWriter
{
  public string Upsert(DrawingToPublish drawing, List<string> objectIds)
  {
    var modelCard = new SenderModelCard()
    {
      ModelCardId = Guid.NewGuid().ToString("N"),
      ModelId = drawing.ModelId,
      ProjectId = drawing.ProjectId,
      AccountId = drawing.AccountId,
      WorkspaceId = drawing.WorkspaceId,
      WorkspaceSlug = drawing.WorkspaceSlug,
      ServerUrl = drawing.ServerUrl?.ToString(),
      SendFilter = new AutocadSelectionFilter { SelectedObjectIds = objectIds },
    };

    if (store.GetModelById(modelCard.ModelCardId) != null)
    {
      store.UpdateModel(modelCard);
    }
    else
    {
      store.AddModel(modelCard);
    }

    return modelCard.ModelCardId;
  }
}
