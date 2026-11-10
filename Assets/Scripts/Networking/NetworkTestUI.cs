using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace GraveRobbers.Networking
{
    /// <summary>
    /// Minimal Host / Client buttons for local co-op testing.
    /// </summary>
    public class NetworkTestUI : MonoBehaviour
    {
        const string HostPortKey = "GraveRobbers.HostPort";
        const int PortSearchCount = 20;

        [SerializeField] Camera lobbyCamera;
        AudioListener _lobbyListener;
        ushort _shownPort = 7777;

        void Awake()
        {
            if (lobbyCamera == null)
            {
                var go = GameObject.Find("LobbyCamera");
                if (go != null)
                    lobbyCamera = go.GetComponent<Camera>();
            }

            if (lobbyCamera != null)
                _lobbyListener = lobbyCamera.GetComponent<AudioListener>();
        }

        void OnGUI()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
                return;

            if (nm.IsClient || nm.IsServer)
            {
                SetLobbyActive(false);
                return;
            }

            SetLobbyActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            const float w = 160f;
            const float h = 40f;
            float x = 20f;
            float y = 20f;

            GUI.Label(new Rect(x, y, 460f, 24f), $"Grave Robbers — Host then Client (max 4, port {_shownPort})");
            y += 28f;

            if (GUI.Button(new Rect(x, y, w, h), "Host"))
                StartHost(nm);

            if (GUI.Button(new Rect(x, y + h + 10f, w, h), "Client"))
                StartClient(nm);
        }

        void StartHost(NetworkManager nm)
        {
            var transport = nm.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null)
            {
                nm.StartHost();
                return;
            }

            var data = transport.ConnectionData;
            ushort preferred = data.Port == 0 ? (ushort)7777 : data.Port;
            ushort port = FindFreeLoopbackPort(preferred);
            if (port != preferred)
            {
                Debug.LogWarning(
                    $"UDP {preferred} is already in use (often a previous Play Mode that did not release the socket). Hosting on {port}.");
            }

            data.Port = port;
            if (string.IsNullOrEmpty(data.Address))
                data.Address = "127.0.0.1";
            transport.ConnectionData = data;
            _shownPort = port;

            PlayerPrefs.SetInt(HostPortKey, port);
            PlayerPrefs.Save();

            if (!nm.StartHost())
                Debug.LogError($"Host failed to start on UDP {port}.");
        }

        void StartClient(NetworkManager nm)
        {
            var transport = nm.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport != null && PlayerPrefs.HasKey(HostPortKey))
            {
                var data = transport.ConnectionData;
                data.Port = (ushort)PlayerPrefs.GetInt(HostPortKey);
                if (string.IsNullOrEmpty(data.Address))
                    data.Address = "127.0.0.1";
                transport.ConnectionData = data;
                _shownPort = data.Port;
            }

            nm.StartClient();
        }

        static ushort FindFreeLoopbackPort(ushort start)
        {
            for (int i = 0; i < PortSearchCount; i++)
            {
                ushort port = (ushort)(start + i);
                if (IsLoopbackPortFree(port))
                    return port;
            }

            return start;
        }

        static bool IsLoopbackPortFree(ushort port)
        {
            try
            {
                using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        void SetLobbyActive(bool active)
        {
            if (lobbyCamera != null && lobbyCamera.enabled != active)
                lobbyCamera.enabled = active;

            if (_lobbyListener != null && _lobbyListener.enabled != active)
                _lobbyListener.enabled = active;
        }
    }
}
