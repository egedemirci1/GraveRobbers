using Unity.Netcode;
using UnityEngine;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// Server-spawns loot when the session starts.
    /// Prefabs must already be on the NetworkManager NetworkPrefabsList (setup menu).
    /// </summary>
    public class LootSpawner : MonoBehaviour
    {
        [SerializeField] GameObject[] lootPrefabs;
        [SerializeField] Vector3[] spawnPositions;

        NetworkManager _nm;

        void Start()
        {
            _nm = FindFirstObjectByType<NetworkManager>();
            if (_nm == null)
            {
                Debug.LogError("LootSpawner: NetworkManager not found.");
                return;
            }

            _nm.OnServerStarted += SpawnAll;
        }

        void OnDestroy()
        {
            if (_nm != null)
                _nm.OnServerStarted -= SpawnAll;
        }

        void SpawnAll()
        {
            if (_nm == null || !_nm.IsServer)
                return;

            if (lootPrefabs == null || lootPrefabs.Length == 0 || spawnPositions == null)
                return;

            for (int i = 0; i < spawnPositions.Length; i++)
            {
                GameObject prefab = lootPrefabs[i % lootPrefabs.Length];
                if (prefab == null)
                    continue;

                GameObject instance = Instantiate(prefab, spawnPositions[i], Quaternion.identity);
                var netObj = instance.GetComponent<NetworkObject>();
                if (netObj == null)
                {
                    Destroy(instance);
                    continue;
                }

                netObj.Spawn(true);
            }
        }

#if UNITY_EDITOR
        public void EditorConfigure(GameObject[] prefabs, Vector3[] positions)
        {
            lootPrefabs = prefabs;
            spawnPositions = positions;
        }
#endif
    }
}
