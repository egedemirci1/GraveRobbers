using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// Server-authoritative loot. First successful claim wins; later pickups are rejected.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public class LootItem : NetworkBehaviour
    {
        [SerializeField] float weight = 1f;
        [SerializeField] MeshRenderer meshRenderer;
        [SerializeField] Color highlightColor = new Color(1f, 0.85f, 0.2f);

        public NetworkVariable<bool> IsHeld = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public NetworkVariable<ulong> HeldByClientId = new(
            ulong.MaxValue,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        Rigidbody _body;
        NetworkTransform _networkTransform;
        NetworkRigidbody _networkRigidbody;
        RigidbodyInterpolation _freeInterpolation = RigidbodyInterpolation.Interpolate;
        MeshRenderer[] _renderers = System.Array.Empty<MeshRenderer>();
        Color _baseColor = Color.white;
        bool _highlighted;

        public float Weight => weight;

        public bool IsWeapon
        {
            get
            {
                string itemName = name;
                if (!itemName.StartsWith("Weapon_"))
                    return false;

                return itemName.IndexOf("Smoke", System.StringComparison.OrdinalIgnoreCase) < 0
                    && itemName.IndexOf("RGD", System.StringComparison.OrdinalIgnoreCase) < 0;
            }
        }

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
            _networkRigidbody = GetComponent<NetworkRigidbody>();
            if (_body != null)
                _freeInterpolation = _body.interpolation;
            if (meshRenderer != null)
                _renderers = new[] { meshRenderer };
            else
                _renderers = GetComponentsInChildren<MeshRenderer>(true);

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null && _renderers[i].sharedMaterial != null)
                {
                    _baseColor = ReadColor(_renderers[i].sharedMaterial);
                    break;
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            IsHeld.OnValueChanged += OnHeldChanged;
            ApplyHeldVisualState(IsHeld.Value);
        }

        public override void OnNetworkDespawn()
        {
            IsHeld.OnValueChanged -= OnHeldChanged;
            base.OnNetworkDespawn();
        }

        void OnHeldChanged(bool previous, bool current) => ApplyHeldVisualState(current);

        void ApplyHeldVisualState(bool held)
        {
            if (_networkTransform != null)
                _networkTransform.enabled = !held;

            if (_body == null)
                return;

            if (held)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.interpolation = RigidbodyInterpolation.None;
                _body.useGravity = false;
                _body.detectCollisions = false;
                if (_networkRigidbody != null)
                {
                    _networkRigidbody.AutoUpdateKinematicState = false;
                    _networkRigidbody.SetIsKinematic(true);
                }
                else
                {
                    _body.isKinematic = true;
                }
            }
            else
            {
                _body.interpolation = _freeInterpolation;
                _body.useGravity = true;
                _body.detectCollisions = true;
                if (_networkRigidbody != null)
                {
                    _networkRigidbody.AutoUpdateKinematicState = true;
                    _networkRigidbody.SetIsKinematic(!IsServer);
                }
                else
                {
                    _body.isKinematic = !IsServer;
                }
            }
        }

        /// <summary>
        /// Puts the carried mesh on the hands. Netcode only accepts another NetworkObject as a
        /// parent, so the hand bone cannot own this object. Every peer places it after the pose.
        /// </summary>
        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (_body == null)
                return;

            _body.position = position;
            _body.rotation = rotation;
        }

        public void SetHighlight(bool on)
        {
            if (_renderers == null || _renderers.Length == 0 || _highlighted == on)
                return;

            _highlighted = on;
            ApplyColor(on ? highlightColor : _baseColor);
        }

        /// <summary>
        /// Server only. Atomic claim: sets IsHeld immediately so concurrent pickups lose.
        /// </summary>
        public bool ServerTryBeginHold(Transform holdPoint, ulong holderClientId)
        {
            if (!IsServer)
                return false;

            // First writer wins — server processes RPCs one at a time.
            if (IsHeld.Value)
                return false;

            IsHeld.Value = true;
            HeldByClientId.Value = holderClientId;

            // HoldPoint has no NetworkObject, and Netcode refuses that parent.
            // Attach to the player itself; PickupSystem then moves the mesh onto the hands.
            NetworkObject player = holdPoint.GetComponentInParent<NetworkObject>();
            if (player != null)
                NetworkObject.TrySetParent(player, worldPositionStays: true);

            SetHighlight(false);
            return true;
        }

        /// <summary>Server only. Drops only if this client currently holds the item.</summary>
        public bool ServerTryEndHold(ulong holderClientId, Vector3 throwImpulse)
        {
            if (!IsServer)
                return false;

            if (!IsHeld.Value || HeldByClientId.Value != holderClientId)
                return false;

            NetworkObject.TryRemoveParent(worldPositionStays: true);

            _body.isKinematic = false;
            _body.detectCollisions = true;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;

            if (throwImpulse.sqrMagnitude > 0.001f)
                _body.AddForce(throwImpulse, ForceMode.Impulse);

            IsHeld.Value = false;
            HeldByClientId.Value = ulong.MaxValue;
            return true;
        }

        void ApplyColor(Color color)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                MeshRenderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                var mat = renderer.material;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", color);
                else
                    mat.color = color;
            }
        }

        static Color ReadColor(Material mat)
        {
            if (mat.HasProperty("_BaseColor"))
                return mat.GetColor("_BaseColor");
            return mat.color;
        }
    }
}
