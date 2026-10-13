using System.Collections.Generic;
using UnityEngine;

namespace GraveRobbers.Networking
{
    /// <summary>
    /// Server claims free spawn slots so up to 4 players never share a point.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        static readonly List<SpawnPoint> All = new();
        static readonly Dictionary<ulong, SpawnPoint> ClaimedByClient = new();

        [SerializeField] int index;

        public int Index => index;
        public bool IsFree { get; private set; } = true;

        void OnEnable()
        {
            if (!All.Contains(this))
                All.Add(this);
            All.Sort((a, b) => a.index.CompareTo(b.index));
        }

        void OnDisable()
        {
            All.Remove(this);
        }

        public void EditorSetIndex(int value) => index = value;

        /// <summary>Server only. Returns null if no free slot.</summary>
        public static SpawnPoint ServerClaim(ulong clientId)
        {
            if (ClaimedByClient.TryGetValue(clientId, out SpawnPoint existing) && existing != null)
                return existing;

            for (int i = 0; i < All.Count; i++)
            {
                SpawnPoint point = All[i];
                if (point == null || !point.IsFree)
                    continue;

                point.IsFree = false;
                ClaimedByClient[clientId] = point;
                return point;
            }

            return null;
        }

        /// <summary>Server only.</summary>
        public static void ServerRelease(ulong clientId)
        {
            if (!ClaimedByClient.TryGetValue(clientId, out SpawnPoint point))
                return;

            ClaimedByClient.Remove(clientId);
            if (point != null)
                point.IsFree = true;
        }

        public static void ServerResetAll()
        {
            ClaimedByClient.Clear();
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] != null)
                    All[i].IsFree = true;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsFree ? Color.cyan : Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward);
        }
    }
}
