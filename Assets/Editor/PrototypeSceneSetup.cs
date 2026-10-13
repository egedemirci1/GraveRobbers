using GraveRobbers.Interaction;
using GraveRobbers.Networking;
using GraveRobbers.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GraveRobbers.Editor
{
    /// <summary>
    /// Builds co-op prototype: ground, NetworkManager, spawn points, player + loot prefabs.
    /// Menu: Grave Robbers / Setup Co-op Prototype Scene
    /// </summary>
    public static class PrototypeSceneSetup
    {
        const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string ScenePath = "Assets/Scenes/Prototype.unity";
        const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        const string LootPrefabFolder = "Assets/_Project/Prefabs/Loot";
        const int LootLayer = 6; // TagManager: "Loot"

        [MenuItem("Grave Robbers/Setup Co-op Prototype Scene")]
        public static void Setup()
        {
            EnsureLootLayer();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            CreateGround();
            CreateSpawnPoints();
            CreateLobbyCamera();

            GameObject[] lootPrefabs = BuildAndSaveLootPrefabs();
            GameObject playerPrefab = BuildAndSavePlayerPrefab();
            CreateNetworkManager(playerPrefab, lootPrefabs);
            CreateLootSpawner(lootPrefabs);
            CreateNetworkUi();

            DirectoryEnsure("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            Debug.Log(
                "Co-op prototype ready (2-4 players): Host/Client, E pick/drop, LMB throw. " +
                "Max 4 connections, 4 spawn points, ClientId colors.");
        }

        [MenuItem("Grave Robbers/Setup Day 1 Prototype Scene")]
        public static void SetupLegacy() => Setup();

        static void EnsureLootLayer()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            SerializedProperty slot = layers.GetArrayElementAtIndex(LootLayer);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = "Loot";
                tagManager.ApplyModifiedProperties();
            }
        }

        static void CreateLighting()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            lightGo.AddComponent<UniversalAdditionalLightData>();

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.35f, 0.38f, 0.45f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.22f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.08f);
        }

        static void CreateGround()
        {
            ProBuilderMesh ground = ShapeGenerator.GeneratePlane(
                PivotLocation.Center,
                width: 50,
                height: 50,
                widthCuts: 0,
                heightCuts: 0,
                axis: Axis.Up);

            ground.gameObject.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.GetComponent<MeshRenderer>().sharedMaterial =
                CreateLitMaterial("GroundGrey", new Color(0.28f, 0.3f, 0.27f));

            if (ground.GetComponent<MeshCollider>() == null)
                ground.gameObject.AddComponent<MeshCollider>();
        }

        static void CreateSpawnPoints()
        {
            Vector3[] positions =
            {
                new Vector3(-3f, 1f, 0f),
                new Vector3(3f, 1f, 0f),
                new Vector3(0f, 1f, 3f),
                new Vector3(0f, 1f, -3f)
            };

            for (int i = 0; i < positions.Length; i++)
            {
                var go = new GameObject($"SpawnPoint_{i}");
                go.transform.position = positions[i];
                var point = go.AddComponent<SpawnPoint>();
                point.EditorSetIndex(i);
            }
        }

        static GameObject[] BuildAndSaveLootPrefabs()
        {
            DirectoryEnsure("Assets/_Project");
            DirectoryEnsure("Assets/_Project/Prefabs");
            DirectoryEnsure(LootPrefabFolder);

            var defs = new (string name, float size, float mass, Color color)[]
            {
                ("Loot_Small", 0.35f, 1f, new Color(0.75f, 0.7f, 0.45f)),
                ("Loot_Medium", 0.55f, 4f, new Color(0.55f, 0.6f, 0.75f)),
                ("Loot_Large", 0.85f, 10f, new Color(0.65f, 0.4f, 0.4f))
            };

            var prefabs = new GameObject[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                prefabs[i] = CreateLootPrefab(defs[i].name, defs[i].size, defs[i].mass, defs[i].color);
            }

            return prefabs;
        }

        static GameObject CreateLootPrefab(string name, float size, float mass, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = LootLayer;
            go.transform.localScale = Vector3.one * size;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();
            go.AddComponent<NetworkRigidbody>();

            var loot = go.AddComponent<LootItem>();
            var mat = CreateLitMaterial(name + "_Mat", color);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;

            var so = new SerializedObject(loot);
            so.FindProperty("weight").floatValue = mass;
            so.FindProperty("meshRenderer").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = $"{LootPrefabFolder}/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject BuildAndSavePlayerPrefab()
        {
            var player = new GameObject("Player");
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 1f, 0f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.skinWidth = 0.08f;
            controller.minMoveDistance = 0f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            Material bodyMat = CreateLitMaterial("PlayerBody", new Color(0.55f, 0.55f, 0.6f));
            body.GetComponent<MeshRenderer>().sharedMaterial = bodyMat;

            var cameraPivot = new GameObject("CameraPivot");
            cameraPivot.transform.SetParent(player.transform, false);
            cameraPivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var cameraGo = new GameObject("PlayerCamera");
            cameraGo.transform.SetParent(cameraPivot.transform, false);
            cameraGo.transform.localPosition = Vector3.zero;
            cameraGo.tag = "MainCamera";

            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.enabled = false;
            cameraGo.AddComponent<AudioListener>().enabled = false;
            cameraGo.AddComponent<UniversalAdditionalCameraData>();

            var holdPoint = new GameObject("HoldPoint");
            holdPoint.transform.SetParent(cameraPivot.transform, false);
            holdPoint.transform.localPosition = new Vector3(0.35f, -0.25f, 1.1f);

            player.AddComponent<NetworkObject>();
            player.AddComponent<ClientNetworkTransform>();
            var playerController = player.AddComponent<PlayerController>();
            var pickup = player.AddComponent<PickupSystem>();

            var inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            var so = new SerializedObject(playerController);
            so.FindProperty("inputActions").objectReferenceValue = inputAsset;
            so.FindProperty("cameraTransform").objectReferenceValue = cameraPivot.transform;
            so.FindProperty("bodyRenderer").objectReferenceValue = body.GetComponent<MeshRenderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var pso = new SerializedObject(pickup);
            pso.FindProperty("holdPoint").objectReferenceValue = holdPoint.transform;
            pso.FindProperty("rayOrigin").objectReferenceValue = cameraGo.transform;
            pso.FindProperty("playerController").objectReferenceValue = playerController;
            pso.FindProperty("lootMask").intValue = 1 << LootLayer;
            pso.ApplyModifiedPropertiesWithoutUndo();

            DirectoryEnsure("Assets/_Project");
            DirectoryEnsure("Assets/_Project/Prefabs");

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);
            return prefab;
        }

        static void CreateNetworkManager(GameObject playerPrefab, GameObject[] lootPrefabs)
        {
            var go = new GameObject("NetworkManager");
            var nm = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            go.AddComponent<SessionLimits>();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.PlayerPrefab = playerPrefab;
            nm.NetworkConfig.ConnectionApproval = true;
            // Avoid Host/Client config-hash mismatch (common with MPPM + runtime prefab adds).
            nm.NetworkConfig.ForceSamePrefabs = false;

            NetworkPrefabsList prefabList = BuildNetworkPrefabsList(playerPrefab, lootPrefabs);
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);
        }

        static NetworkPrefabsList BuildNetworkPrefabsList(GameObject playerPrefab, GameObject[] lootPrefabs)
        {
            const string listPath = "Assets/_Project/GraveRobbersNetworkPrefabs.asset";
            DirectoryEnsure("Assets/_Project");

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(listPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, listPath);
            }

            // Rebuild list so Host/Client always share the same registered prefabs.
            while (list.PrefabList.Count > 0)
                list.Remove(list.PrefabList[0]);

            if (playerPrefab != null)
                list.Add(new NetworkPrefab { Prefab = playerPrefab });

            if (lootPrefabs != null)
            {
                foreach (GameObject loot in lootPrefabs)
                {
                    if (loot != null)
                        list.Add(new NetworkPrefab { Prefab = loot });
                }
            }

            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            return list;
        }

        static void CreateLootSpawner(GameObject[] lootPrefabs)
        {
            var go = new GameObject("LootSpawner");
            var spawner = go.AddComponent<LootSpawner>();

            Vector3[] positions =
            {
                new Vector3(1.5f, 0.5f, 2f),
                new Vector3(-1.5f, 0.5f, 2.5f),
                new Vector3(2.5f, 0.5f, -1f),
                new Vector3(-2f, 0.5f, -2f),
                new Vector3(0f, 0.5f, 4f),
                new Vector3(4f, 0.5f, 1f),
                new Vector3(-4f, 0.5f, 0f),
                new Vector3(1f, 0.5f, -4f)
            };

            spawner.EditorConfigure(lootPrefabs, positions);
            EditorUtility.SetDirty(spawner);
        }

        static void CreateLobbyCamera()
        {
            var go = new GameObject("LobbyCamera");
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 8f, -12f);
            go.transform.rotation = Quaternion.Euler(30f, 0f, 0f);

            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            go.AddComponent<AudioListener>();
            go.AddComponent<UniversalAdditionalCameraData>();
        }

        static void CreateNetworkUi()
        {
            var go = new GameObject("NetworkTestUI");
            var ui = go.AddComponent<NetworkTestUI>();
            var lobby = GameObject.Find("LobbyCamera");
            if (lobby != null)
            {
                var so = new SerializedObject(ui);
                so.FindProperty("lobbyCamera").objectReferenceValue = lobby.GetComponent<Camera>();
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static Material CreateLitMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            DirectoryEnsure("Assets/Materials");
            string path = $"Assets/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.HasProperty("_BaseColor"))
                    existing.SetColor("_BaseColor", color);
                else
                    existing.color = color;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void DirectoryEnsure(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
            {
                if (!AssetDatabase.IsValidFolder(parent))
                    DirectoryEnsure(parent);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }
}
