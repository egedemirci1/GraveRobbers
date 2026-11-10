using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GraveRobbers.Interaction;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GraveRobbers.Editor
{
    /// <summary>
    /// Turns the low-poly gun models into the same server-auth pickups as the loot cubes
    /// and lines them up in the Prototype scene.
    /// </summary>
    public static class WeaponPickupSetup
    {
        const string ScenePath = "Assets/Scenes/Prototype.unity";
        const string WeaponFolder = "Assets/_Project/Prefabs/Weapons";
        const string PrefabListPath = "Assets/_Project/GraveRobbersNetworkPrefabs.asset";
        const string VisualRoot = "Assets/Low Poly Weapons VOL.1/Prefabs";
        const int LootLayer = 6;
        const string FlagName = "grave_robbers_add_weapons.flag";

        static readonly string[] WeaponNames =
        {
            "M1911",
            "Uzi",
            "AK74",
            "M4_8",
            "Bennelli_M4",
            "M249",
            "M107",
            "RPG7",
            "M2_50cal",
            "RGD-5",
            "Smoke"
        };

        static bool _waitingForPlayMode;

        [InitializeOnLoadMethod]
        static void ScheduleRequestedSetup()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        [MenuItem("Grave Robbers/Add Weapons To Prototype")]
        public static void AddWeaponsToPrototype()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Leave Play Mode, then run Grave Robbers/Add Weapons To Prototype.");
                return;
            }

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                {
                    Debug.LogError("Save the open scene, then run Grave Robbers/Add Weapons To Prototype.");
                    return;
                }

                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            var created = new List<GameObject>();
            foreach (string weaponName in WeaponNames)
            {
                GameObject prefab = CreateWeaponPrefab(weaponName);
                if (prefab != null)
                    created.Add(prefab);
            }

            if (created.Count == 0)
            {
                Debug.LogError("No weapon models found under " + VisualRoot);
                return;
            }

            foreach (GameObject weapon in created)
                AssignNetworkHash(weapon.GetComponent<NetworkObject>());

            RegisterPrefabs(created);
            PlaceInScene(scene, created);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Placed {created.Count} weapons in Prototype. Look at one and press E.");
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
            AddWeaponsToPrototype();
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _waitingForPlayMode = false;
            EditorApplication.delayCall += RunIfRequested;
        }

        static GameObject CreateWeaponPrefab(string weaponName)
        {
            string visualPath = $"{VisualRoot}/{weaponName}.prefab";
            var visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            if (visualPrefab == null)
            {
                Debug.LogWarning("Missing weapon prefab: " + visualPath);
                return null;
            }

            DirectoryEnsure(WeaponFolder);

            var root = new GameObject("Weapon_" + weaponName);
            root.layer = LootLayer;

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab);
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            SetLayerRecursively(root, LootLayer);

            var box = root.AddComponent<BoxCollider>();
            FitCollider(root.transform, box);

            float mass = MassFor(weaponName);
            var body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>();
            root.AddComponent<NetworkRigidbody>();

            var loot = root.AddComponent<LootItem>();
            var so = new SerializedObject(loot);
            so.FindProperty("weight").floatValue = mass;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = $"{WeaponFolder}/Weapon_{weaponName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void RegisterPrefabs(List<GameObject> weaponPrefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                Debug.LogError("Network prefab list missing: " + PrefabListPath);
                return;
            }

            for (int i = list.PrefabList.Count - 1; i >= 0; i--)
            {
                GameObject existing = list.PrefabList[i].Prefab;
                if (existing != null && existing.name.StartsWith("Weapon_"))
                    list.Remove(list.PrefabList[i]);
            }

            foreach (GameObject weapon in weaponPrefabs)
            {
                if (weapon != null)
                    list.Add(new NetworkPrefab { Prefab = weapon });
            }

            EditorUtility.SetDirty(list);
        }

        static void PlaceInScene(UnityEngine.SceneManagement.Scene scene, List<GameObject> weaponPrefabs)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith("Weapon_"))
                    Object.DestroyImmediate(root);
            }

            const float spacing = 2.4f;
            float originX = -((weaponPrefabs.Count - 1) * spacing) * 0.5f;
            for (int i = 0; i < weaponPrefabs.Count; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefabs[i], scene);
                var collider = instance.GetComponent<BoxCollider>();
                float y = collider != null ? collider.bounds.extents.y + 0.05f : 0.4f;
                instance.transform.SetPositionAndRotation(
                    new Vector3(originX + i * spacing, y, -8f),
                    Quaternion.identity);
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (!root.name.StartsWith("Weapon_"))
                    continue;

                AssignNetworkHash(root.GetComponent<NetworkObject>());
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void AssignNetworkHash(NetworkObject networkObject)
        {
            if (networkObject == null)
                return;

            MethodInfo validate = typeof(NetworkObject).GetMethod(
                "OnValidate",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            validate?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
        }

        static void FitCollider(Transform root, BoxCollider box)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            bool hasPoint = false;
            Bounds local = new Bounds(Vector3.zero, Vector3.zero);

            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                Vector3 extents = world.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = world.center + Vector3.Scale(extents, new Vector3(x, y, z));
                            Vector3 point = root.InverseTransformPoint(corner);
                            if (!hasPoint)
                            {
                                local = new Bounds(point, Vector3.zero);
                                hasPoint = true;
                            }
                            else
                            {
                                local.Encapsulate(point);
                            }
                        }
                    }
                }
            }

            if (!hasPoint)
            {
                box.center = new Vector3(0f, 0.1f, 0f);
                box.size = new Vector3(0.25f, 0.25f, 0.7f);
                return;
            }

            box.center = local.center;
            box.size = Vector3.Max(local.size, new Vector3(0.2f, 0.2f, 0.2f));
        }

        static float MassFor(string weaponName)
        {
            switch (weaponName)
            {
                case "M249":
                case "M107":
                case "RPG7":
                case "M2_50cal":
                    return 6f;
                case "AK74":
                case "M4_8":
                case "Bennelli_M4":
                    return 3f;
                default:
                    return 1.5f;
            }
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        static void DirectoryEnsure(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                DirectoryEnsure(parent);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
                AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
