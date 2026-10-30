using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using PPDL = Autodesk.ProcessPower.DataLinks;
using SpecContainer = Speckle.Bundle.Spec.Container;

namespace Speckle.Connectors.Autocad.Operations.Send;

public partial class AutocadArtifactRootObjectBuilder
{
  private static readonly string[] s_plant3dKeys = ["P&ID", "Plant3D Data"];

  private const string LINE_GROUP_SPECKLE_TYPE = "Objects.Data.DataObject";

  // Line groups (P&ID PipeLineGroup / SignalLineGroup, 3D piping LineGroup) own no geometry: the group's own Data Links
  // row is read here (UI thread, DataLinks is document-bound) so phase 2 can publish it as a geometry-less object.
  partial void CollectVerticalData(
    IReadOnlyList<CollectedObject> objects,
    Dictionary<string, Dictionary<string, object?>> verticalProperties
  )
  {
    PPDL.DataLinksManager? dataLinksManager = null;
    foreach (var co in objects)
    {
      if (
        !TryGetPlant3dDataManager(co.Properties, out var dataManager)
        || !TryGetLineGroup(dataManager, out string groupKey, out int groupId, out _)
        || verticalProperties.ContainsKey(groupKey)
      )
      {
        continue;
      }

      dataLinksManager ??= PPDL.DataLinksManager.GetManager(converterSettings.Current.Document.Database);
      if (dataLinksManager is null)
      {
        return;
      }

      var properties = new Dictionary<string, object?>();
      try
      {
        var rowProperties = dataLinksManager.GetAllProperties(groupId, true);
        if (rowProperties is not null)
        {
          foreach (var kvp in rowProperties)
          {
            properties[kvp.Key ?? "unknown"] = kvp.Value;
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(ex, "Failed to read Data Links properties for line group {GroupId}", groupId);
      }

      verticalProperties[groupKey] = properties;
    }
  }

  // Plant 3D Data Manager → relations, grounded in the "Data Manager" props written by Plant3dDataExtractor:
  //   IN_SYSTEM  part → CONTAINER("MEP System") named by Line Number; the line group's own object (no geometry,
  //              carrying its Data Links properties) joins the same container, so group ↔ segments navigate via it.
  //   class tree part → leaf CONTAINER("Group"), each level def_ref'd to its parent (nesting is a column, not an edge).
  partial void EmitVerticalRelations(
    ObjectsArtifactPipeline pipeline,
    CollectedModel model,
    HashSet<string> definitionMemberIds
  )
  {
    var lineGroupKById = new Dictionary<string, int>(StringComparer.Ordinal);
    var classKByPath = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var co in model.Objects)
    {
      if (
        definitionMemberIds.Contains(co.ApplicationId) || !TryGetPlant3dDataManager(co.Properties, out var dataManager)
      )
      {
        continue;
      }

      int objK = pipeline.InternObject(co.ApplicationId);

      AddLineGroupAsSystem(pipeline, model, dataManager, lineGroupKById, objK);
      AddClassHierarchyAsNestedGroups(pipeline, dataManager, classKByPath, objK);
    }
  }

  private static void AddLineGroupAsSystem(
    ObjectsArtifactPipeline pipeline,
    CollectedModel model,
    IReadOnlyDictionary<string, object?> dataManager,
    Dictionary<string, int> lineGroupKById,
    int objK
  )
  {
    if (!TryGetLineGroup(dataManager, out string groupKey, out int groupId, out string groupType))
    {
      return;
    }

    if (!lineGroupKById.TryGetValue(groupKey, out int groupK))
    {
      string lineNumber = (dataManager.TryGetValue("Line Number", out var ln) ? ln as string : null) ?? groupKey;
      if (string.IsNullOrWhiteSpace(lineNumber))
      {
        lineNumber = $"{groupType} {groupId}";
      }
      groupK = pipeline.AddContainer(groupKey, new SpecContainer(lineNumber, null, "MEP System", null));
      lineGroupKById[groupKey] = groupK;

      AddLineGroupObject(pipeline, model, groupKey, groupId, groupType, lineNumber, groupK);
    }
    pipeline.InSystem(objK, groupK, 0);
  }

  // The bundle has no per-container property store, so the group's properties ride a geometry-less object that is
  // IN_SYSTEM of the group's own container. Geometry stays solely on the member segments (no duplication).
  private static void AddLineGroupObject(
    ObjectsArtifactPipeline pipeline,
    CollectedModel model,
    string groupKey,
    int groupId,
    string groupType,
    string lineNumber,
    int groupK
  )
  {
    var properties = model.VerticalProperties.TryGetValue(groupKey, out var collected)
      ? new Dictionary<string, object?>(collected)
      : new Dictionary<string, object?>();
    properties["Group Id"] = groupId;
    properties["Group Type"] = groupType;
    properties["Line Number"] = lineNumber;

    string groupObjectId = $"p3d-linegroup:{groupType}:{groupId}";
    int groupObjK = pipeline.InternObject(groupObjectId);
    pipeline.AddProperties(
      groupObjectId,
      properties,
      RootScalars(LINE_GROUP_SPECKLE_TYPE, lineNumber, model.Units, groupType)
    );
    pipeline.InSystem(groupObjK, groupK, 0);
  }

  private static bool TryGetLineGroup(
    IReadOnlyDictionary<string, object?> dataManager,
    out string groupKey,
    out int groupId,
    out string groupType
  )
  {
    groupKey = string.Empty;
    groupId = 0;
    groupType = string.Empty;
    if (
      !dataManager.TryGetValue("Group Id", out var gid)
      || gid is null
      || !dataManager.TryGetValue("Group Type", out var gt)
      || gt is not string type
    )
    {
      return false;
    }

    groupId = Convert.ToInt32(gid, CultureInfo.InvariantCulture);
    groupType = type;
    groupKey = $"p3d:{groupType}:{groupId.ToString(CultureInfo.InvariantCulture)}";
    return groupId > 0;
  }

  private static void AddClassHierarchyAsNestedGroups(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<string, object?> dataManager,
    Dictionary<string, int> classKByPath,
    int objK
  )
  {
    var levels = ReadClassLevels(dataManager);
    if (levels.Count == 0)
    {
      return;
    }

    int? parentK = null; // def_ref = parent container's node K (null for the root level)
    int leafK = -1;
    for (int i = 0; i < levels.Count; i++)
    {
      string path = "p3d:class:" + string.Join("/", levels.Take(i + 1)); // stable, unique container id
      if (!classKByPath.TryGetValue(path, out leafK))
      {
        leafK = pipeline.AddContainer(path, new SpecContainer(levels[i], parentK, "Group", null));
        classKByPath[path] = leafK;
      }
      parentK = leafK;
    }
    pipeline.InGroup(objK, leafK, 0);
  }

  private static List<string> ReadClassLevels(IReadOnlyDictionary<string, object?> dataManager)
  {
    var levels = new List<string>();
    for (int i = 1; dataManager.TryGetValue($"Level {i}", out var lvl) && lvl is string s; i++)
    {
      levels.Add(s);
    }
    return levels;
  }

  private static bool TryGetPlant3dDataManager(
    IReadOnlyDictionary<string, object?> properties,
    [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? dataManager
  )
  {
    foreach (var plant3dKey in s_plant3dKeys)
    {
      if (
        TryGetDictionary(properties, plant3dKey, out var propertyGroup)
        && TryGetDictionary(propertyGroup, "Data Manager", out var value)
      )
      {
        dataManager = value;
        return true;
      }
    }

    dataManager = null;
    return false;
  }

  private static bool TryGetDictionary(
    IReadOnlyDictionary<string, object?> properties,
    string key,
    [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? dictionary
  )
  {
    dictionary = properties.TryGetValue(key, out var value) ? value as IReadOnlyDictionary<string, object?> : null;
    return dictionary is not null;
  }
}
