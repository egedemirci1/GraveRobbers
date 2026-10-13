using Unity.Netcode;
using UnityEngine;

namespace GraveRobbers.Networking
{
    /// <summary>
    /// Caps the session at 4 players and keeps NetworkConfig identical on Host/Client.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(NetworkManager))]
    public class SessionLimits : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        NetworkManager _nm;

        void Awake()
        {
            _nm = GetComponent<NetworkManager>();

            // Must match on every peer BEFORE StartHost/StartClient (config hash).
            // ForceSamePrefabs=false avoids MPPM/runtime prefab-list hash mismatches.
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.NetworkConfig.ForceSamePrefabs = false;

            _nm.ConnectionApprovalCallback += ApproveConnection;
            _nm.OnServerStarted += OnServerStarted;
            _nm.OnServerStopped += OnServerStopped;
        }

        void OnDestroy()
        {
            if (_nm == null)
                return;

            _nm.ConnectionApprovalCallback -= ApproveConnection;
            _nm.OnServerStarted -= OnServerStarted;
            _nm.OnServerStopped -= OnServerStopped;
        }

        void OnServerStarted() => SpawnPoint.ServerResetAll();

        void OnServerStopped(bool _) => SpawnPoint.ServerResetAll();

        void ApproveConnection(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            int connected = _nm.ConnectedClientsIds.Count;
            bool hasRoom = connected < MaxPlayers;

            response.Approved = hasRoom;
            response.CreatePlayerObject = hasRoom;
            response.Pending = false;
            response.Reason = hasRoom ? string.Empty : "Session full (max 4 players).";
        }
    }
}
