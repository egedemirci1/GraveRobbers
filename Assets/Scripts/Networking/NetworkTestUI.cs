using Unity.Netcode;
using UnityEngine;

namespace GraveRobbers.Networking
{
    /// <summary>
    /// Minimal Host / Client buttons for local co-op testing.
    /// </summary>
    public class NetworkTestUI : MonoBehaviour
    {
        [SerializeField] Camera lobbyCamera;
        AudioListener _lobbyListener;

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

            GUI.Label(new Rect(x, y, 400f, 24f), "Grave Robbers — Host then Client (max 4)");
            y += 28f;

            if (GUI.Button(new Rect(x, y, w, h), "Host"))
                nm.StartHost();

            if (GUI.Button(new Rect(x, y + h + 10f, w, h), "Client"))
                nm.StartClient();
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
