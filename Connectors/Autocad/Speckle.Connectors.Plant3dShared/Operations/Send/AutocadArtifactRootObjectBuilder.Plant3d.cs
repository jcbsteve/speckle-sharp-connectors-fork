using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Speckle.Objects.Data;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using PPDL = Autodesk.ProcessPower.DataLinks;
using SpecContainer = Speckle.Bundle.Spec.Container;

namespace Speckle.Connectors.Autocad.Operations.Send;

public partial class AutocadArtifactRootObjectBuilder
{
  private static readonly string[] s_plant3dKeys = ["P&ID", "Plant3D Data"];
  private const string LINE_GROUP_OBJECT_SUFFIX = ":object"; // keeps the group's object id distinct from its container id

  // ── Phase 1: collect line-group objects (UI thread) ──
  // No geometry; they carry the group's Data Links properties.
  partial void CollectVerticalObjects(List<CollectedObject> objects)
  {
    var collectedGroupKeys = new HashSet<string>(StringComparer.Ordinal);
    var lineGroupObjects = new List<CollectedObject>();

    if (!TryGetDataLinksManager(out var dataLinksManager))
    {
      return;
    }

    foreach (var co in objects)
    {
      if (
        !TryGetDataManagerProperties(co.Properties, out var dataManagerProperties)
        || !TryGetLineGroup(dataManagerProperties, out var group)
        || !collectedGroupKeys.Add(group.Key)
      )
      {
        continue;
      }

      var properties = ReadDataLinksProperties(dataLinksManager, group.Id);
      properties["Group Id"] = group.Id;
      properties["Group Type"] = group.Type;
      properties["Line Number"] = group.LineNumber;

      var lineGroupObject = new DataObject
      {
        name = group.Name,
        properties = properties,
        displayValue = [],
        applicationId = group.ObjectId,
      };

      // Layer: the first member's layer keeps it in the default IN_COLLECTION scene view.
      lineGroupObjects.Add(
        new CollectedObject(lineGroupObject.applicationId, group.Type, co.LayerName, properties, lineGroupObject)
      );
    }

    objects.AddRange(lineGroupObjects);
  }

  private bool TryGetDataLinksManager([NotNullWhen(true)] out PPDL.DataLinksManager? dataLinksManager)
  {
    try
    {
      dataLinksManager = PPDL.DataLinksManager.GetManager(converterSettings.Current.Document.Database);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Plant 3D DataLinksManager unavailable");
      dataLinksManager = null;
    }

    return dataLinksManager is not null;
  }

  // Must run on the UI thread (DataLinks is document-bound).
  private Dictionary<string, object?> ReadDataLinksProperties(PPDL.DataLinksManager dataLinksManager, int rowId)
  {
    var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
    try
    {
      var rowProperties = dataLinksManager.GetAllProperties(rowId, true);
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
      logger.LogWarning(ex, "Failed to read Data Links properties for line group {GroupId}", rowId);
    }

    return properties;
  }

  // ── Phase 2: emit relations (worker thread) ──
  // Builds relations from the "Data Manager" properties written by Plant3dDataExtractor:
  //   IN_SYSTEM: part → "MEP System" container per line group; the group's own object joins it too.
  //   Class tree: part → leaf "Group" container; each level def_refs its parent.
  partial void EmitVerticalRelations(
    ObjectsArtifactPipeline pipeline,
    CollectedModel model,
    HashSet<string> definitionMemberIds
  )
  {
    var lineGroupKByKey = new Dictionary<string, int>(StringComparer.Ordinal);
    var classKByPath = new Dictionary<string, int>(StringComparer.Ordinal);
    var collectedObjectIds = model.Objects.Select(o => o.ApplicationId).ToHashSet(StringComparer.Ordinal);

    foreach (var co in model.Objects)
    {
      if (
        definitionMemberIds.Contains(co.ApplicationId)
        || !TryGetDataManagerProperties(co.Properties, out var dataManagerProperties)
      )
      {
        continue;
      }

      var objK = pipeline.InternObject(co.ApplicationId);
      AddLineGroupAsSystem(pipeline, dataManagerProperties, lineGroupKByKey, collectedObjectIds, objK);
      AddClassHierarchyAsGroups(pipeline, dataManagerProperties, classKByPath, objK);
    }
  }

  private static void AddLineGroupAsSystem(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<string, object?> dataManagerProperties,
    Dictionary<string, int> lineGroupKByKey,
    HashSet<string> collectedObjectIds,
    int objK
  )
  {
    if (!TryGetLineGroup(dataManagerProperties, out var group))
    {
      return;
    }

    if (!lineGroupKByKey.TryGetValue(group.Key, out var groupK))
    {
      groupK = pipeline.AddContainer(group.Key, new SpecContainer(group.Name, null, "MEP System", null));
      lineGroupKByKey[group.Key] = groupK;

      // Link the group's own object only if phase 1 emitted it; never intern a placeholder.
      var objectId = group.ObjectId;
      if (collectedObjectIds.Contains(objectId))
      {
        pipeline.InSystem(pipeline.InternObject(objectId), groupK, ord: 0);
      }
    }

    pipeline.InSystem(objK, groupK, ord: 0);
  }

  private static void AddClassHierarchyAsGroups(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<string, object?> dataManagerProperties,
    Dictionary<string, int> classKByPath,
    int objK
  )
  {
    var levels = ReadClassLevels(dataManagerProperties);
    if (levels.Count == 0)
    {
      return;
    }

    int? parentK = null; // def_ref = parent container's node K (null for the root level)
    int leafK = -1;
    for (int i = 0; i < levels.Count; i++)
    {
      var path = "p3d:class:" + string.Join("/", levels.Take(i + 1));
      if (!classKByPath.TryGetValue(path, out leafK))
      {
        leafK = pipeline.AddContainer(path, new SpecContainer(levels[i], parentK, "Group", null));
        classKByPath[path] = leafK;
      }
      parentK = leafK;
    }

    pipeline.InGroup(objK, leafK, 0);
  }

  private static List<string> ReadClassLevels(IReadOnlyDictionary<string, object?> dataManagerProperties)
  {
    var levels = new List<string>();
    for (int i = 1; dataManagerProperties.TryGetValue($"Level {i}", out var lvl) && lvl is string s; i++)
    {
      levels.Add(s);
    }
    return levels;
  }

  private readonly record struct LineGroup(int Id, string Type, string LineNumber)
  {
    public string Key { get; } = $"p3d:{Type}:{Id.ToString(CultureInfo.InvariantCulture)}";
    public string Name =>
      string.IsNullOrWhiteSpace(LineNumber) ? $"{Type} {Id.ToString(CultureInfo.InvariantCulture)}" : LineNumber;
    public string ObjectId => Key + LINE_GROUP_OBJECT_SUFFIX;
  }

  private static bool TryGetLineGroup(IReadOnlyDictionary<string, object?> dataManagerProperties, out LineGroup group)
  {
    if (
      !dataManagerProperties.TryGetValue("Group Id", out var gid)
      || gid is not int id
      || id <= 0
      || !dataManagerProperties.TryGetValue("Group Type", out var gt)
      || gt is not string type
    )
    {
      group = default;
      return false;
    }

    var lineNumber = dataManagerProperties.GetValueOrDefault("Line Number")?.ToString() ?? "";
    group = new LineGroup(id, type, lineNumber);
    return true;
  }

  private static bool TryGetDataManagerProperties(
    IReadOnlyDictionary<string, object?> properties,
    [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? dataManagerProperties
  )
  {
    foreach (var plant3dKey in s_plant3dKeys)
    {
      if (
        TryGetDictionary(properties, plant3dKey, out var propertyGroup)
        && TryGetDictionary(propertyGroup, "Data Manager", out var value)
      )
      {
        dataManagerProperties = value;
        return true;
      }
    }

    dataManagerProperties = null;
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
