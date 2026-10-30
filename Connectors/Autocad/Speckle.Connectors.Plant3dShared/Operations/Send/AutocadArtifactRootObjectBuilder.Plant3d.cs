using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Speckle.Objects.Utils;
using SpecContainer = Speckle.Bundle.Spec.Container;

namespace Speckle.Connectors.Autocad.Operations.Send;

public partial class AutocadArtifactRootObjectBuilder
{
  private static readonly string[] s_plant3dKeys = ["P&ID", "Plant3D Data"];

  // Plant 3D Data Manager → relations, grounded in the "Data Manager" props written by Plant3dDataExtractor:
  //   IN_SYSTEM  part → CONTAINER("MEP System") named by Line Number.
  //   class tree part → leaf CONTAINER("Group"), each level def_ref'd to its parent (nesting is a column, not an edge).
  partial void EmitVerticalRelations(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyList<CollectedObject> objects,
    HashSet<string> definitionMemberIds
  )
  {
    var lineGroupKById = new Dictionary<string, int>(StringComparer.Ordinal);
    var classKByPath = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var co in objects)
    {
      if (
        definitionMemberIds.Contains(co.ApplicationId) || !TryGetPlant3dDataManager(co.Properties, out var dataManager)
      )
      {
        continue;
      }

      int objK = pipeline.InternObject(co.ApplicationId);

      AddLineGroupAsSystem(pipeline, dataManager, lineGroupKById, objK);
      AddClassHierarchyAsNestedGroups(pipeline, dataManager, classKByPath, objK);
    }
  }

  private static void AddLineGroupAsSystem(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<string, object?> dataManager,
    Dictionary<string, int> lineGroupKById,
    int objK
  )
  {
    if (
      dataManager.TryGetValue("Group Id", out var gid)
      && gid is not null
      && dataManager.TryGetValue("Group Type", out var gt)
      && gt is string groupType
    )
    {
      string groupKey = $"p3d:{groupType}:{Convert.ToString(gid, CultureInfo.InvariantCulture)}";
      if (!lineGroupKById.TryGetValue(groupKey, out int groupK))
      {
        string lineNumber = (dataManager.TryGetValue("Line Number", out var ln) ? ln as string : null) ?? groupKey;
        groupK = pipeline.AddContainer(groupKey, new SpecContainer(lineNumber, null, "MEP System", null));
        lineGroupKById[groupKey] = groupK;
      }
      pipeline.InSystem(objK, groupK, 0);
    }
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
