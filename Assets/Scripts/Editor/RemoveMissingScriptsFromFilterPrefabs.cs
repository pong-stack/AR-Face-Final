#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Strips behaviours whose script asset is missing (fixes "The referenced script on this Behaviour is missing!").
/// Targets <see cref="FilterPrefabsFolder"/> — the packaged face-filter props prefabs.
/// </summary>
public static class RemoveMissingScriptsFromFilterPrefabs
{
    const string FilterPrefabsFolder = "Assets/_BasicFaceFilter/Prefabs";

    [MenuItem("Tools/Face Filters/Remove Missing Scripts in _BasicFaceFilter/Prefabs")]
    public static void RemoveMissingScriptsInBasicFacePrefabsMenu()
    {
        if (!AssetDatabase.IsValidFolder(FilterPrefabsFolder))
        {
            Debug.LogWarning($"{nameof(RemoveMissingScriptsFromFilterPrefabs)}: Folder not found: '{FilterPrefabsFolder}'.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { FilterPrefabsFolder });
        int modifiedPrefabAssets = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changedInThisPrefab = false;
                foreach (Transform t in contents.GetComponentsInChildren<Transform>(true))
                    changedInThisPrefab |= StripMissingOnSingle(t.gameObject);

                if (!changedInThisPrefab)
                    continue;

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                modifiedPrefabAssets++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log(
            $"{nameof(RemoveMissingScriptsFromFilterPrefabs)}: Processed prefabs under '{FilterPrefabsFolder}'. " +
            $"Saved {(modifiedPrefabAssets)} prefab asset(s) after stripping missing-behaviour stubs.");
    }

    static bool StripMissingOnSingle(GameObject go)
    {
        if (go == null)
            return false;

        int pending = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
        if (pending <= 0)
            return false;

        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
        return true;
    }
}
#endif
