using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GraveRobbers.Editor
{
    /// <summary>
    /// Regenerates NetworkObject GlobalObjectIdHash values left at 0 after
    /// text edits, then rebuilds the shared NetworkPrefabs list.
    /// </summary>
    public static class NetworkPrefabHashFix
    {
        const string PrefabListPath = "Assets/_Project/GraveRobbersNetworkPrefabs.asset";
        const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";
        const string FlagName = "grave_robbers_fix_network_hashes.flag";

        static bool _waitingForPlayMode;

        [InitializeOnLoadMethod]
        static void ScheduleRequestedFix()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        [MenuItem("Grave Robbers/Fix Network Prefab Hashes")]
        public static void Fix()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Leave Play Mode, then run Grave Robbers/Fix Network Prefab Hashes.");
                return;
            }

            int fixedCount = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (RegenerateHash(path))
                    fixedCount++;
            }

            RebuildPrefabList();
            RegenerateSceneHashes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Network prefab hashes refreshed ({fixedCount} prefabs). Stop play on every editor, then rejoin as Host/Client.");
        }

        static void RunIfRequested()
        {
            string flag = Path.Combine(Directory.GetCurrentDirectory(), "Temp", FlagName);
            if (!File.Exists(flag))
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunIfRequested;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (_waitingForPlayMode)
                    return;

                _waitingForPlayMode = true;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                return;
            }

            File.Delete(flag);
            Fix();
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _waitingForPlayMode = false;
            EditorApplication.delayCall += RunIfRequested;
        }

        static bool RegenerateHash(string path)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
                return false;

            NetworkObject[] objects = root.GetComponentsInChildren<NetworkObject>(true);
            if (objects.Length == 0)
                return false;

            // Netcode's own OnValidate writes Hash32(GlobalObjectId) on the prefab asset.
            // Custom hashes diverge between the main editor and a Multiplayer Play Mode clone.
            MethodInfo validate = typeof(NetworkObject).GetMethod(
                "OnValidate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (NetworkObject networkObject in objects)
                validate?.Invoke(networkObject, null);

            EditorUtility.SetDirty(root);
            return true;
        }

        static void RegenerateSceneHashes()
        {
            const string scenePath = "Assets/Scenes/Prototype.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            MethodInfo validate = typeof(NetworkObject).GetMethod(
                "OnValidate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            NetworkObject[] objects = Object.FindObjectsByType<NetworkObject>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            foreach (NetworkObject networkObject in objects)
            {
                if (networkObject.gameObject.scene == scene)
                    validate?.Invoke(networkObject, null);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void RebuildPrefabList()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }

            while (list.PrefabList.Count > 0)
                list.Remove(list.PrefabList[0]);

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            if (player != null)
                list.Add(new NetworkPrefab { Prefab = player });

            string[] lootGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Loot" });
            foreach (string guid in lootGuids)
            {
                GameObject loot = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (loot != null && loot.GetComponent<NetworkObject>() != null)
                    list.Add(new NetworkPrefab { Prefab = loot });
            }

            string[] weaponGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Weapons" });
            foreach (string guid in weaponGuids)
            {
                GameObject weapon = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (weapon != null && weapon.GetComponent<NetworkObject>() != null)
                    list.Add(new NetworkPrefab { Prefab = weapon });
            }

            EditorUtility.SetDirty(list);

            var defaults = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (defaults != null)
            {
                while (defaults.PrefabList.Count > 0)
                    defaults.Remove(defaults.PrefabList[0]);
                foreach (NetworkPrefab entry in list.PrefabList)
                    defaults.Add(new NetworkPrefab { Prefab = entry.Prefab });
                EditorUtility.SetDirty(defaults);
            }
        }
    }
}
