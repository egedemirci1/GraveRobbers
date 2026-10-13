using GraveRobbers.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// Per-player carry. Server validates; only this player's held loot drops on disconnect.
    /// </summary>
    public class PickupSystem : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] Transform holdPoint;
        [SerializeField] Transform rayOrigin;
        [SerializeField] PlayerController playerController;

        [Header("Tuning")]
        [SerializeField] float pickupRange = 3f;
        [SerializeField] float serverMaxDistance = 3.75f;
        [SerializeField] float throwForce = 14f;
        [SerializeField] LayerMask lootMask;

        readonly NetworkVariable<NetworkObjectReference> _heldLootRef = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        LootItem _lookTarget;
        LootItem _serverHeld;

        public bool IsHolding => _heldLootRef.Value.TryGet(out NetworkObject _);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _heldLootRef.OnValueChanged += OnHeldChanged;

            if (playerController == null)
                playerController = GetComponent<PlayerController>();

            if (rayOrigin == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null)
                    rayOrigin = cam.transform;
            }

            if (lootMask.value == 0)
                lootMask = LayerMask.GetMask("Loot");

            RefreshCarrySpeed();
        }

        public override void OnNetworkDespawn()
        {
            _heldLootRef.OnValueChanged -= OnHeldChanged;

            // Only this player's loot — other players' holds are untouched.
            if (IsServer && _serverHeld != null)
                ServerReleaseHeld(Vector3.zero);

            if (IsOwner)
                ClearHighlight();

            base.OnNetworkDespawn();
        }

        void OnHeldChanged(NetworkObjectReference previous, NetworkObjectReference current) => RefreshCarrySpeed();

        void RefreshCarrySpeed()
        {
            if (playerController == null)
                return;

            if (_heldLootRef.Value.TryGet(out NetworkObject netObj) &&
                netObj.TryGetComponent(out LootItem loot))
            {
                float t = Mathf.InverseLerp(1f, 10f, loot.Weight);
                playerController.SpeedMultiplier = Mathf.Lerp(1f, 0.55f, t);
            }
            else
            {
                playerController.SpeedMultiplier = 1f;
            }
        }

        void Update()
        {
            if (!IsOwner || !IsSpawned)
                return;

            UpdateLookTarget();
            HandleInput();
        }

        void UpdateLookTarget()
        {
            LootItem next = null;

            if (!IsHolding && rayOrigin != null &&
                Physics.Raycast(rayOrigin.position, rayOrigin.forward, out RaycastHit hit, pickupRange, lootMask,
                    QueryTriggerInteraction.Ignore))
            {
                next = hit.collider.GetComponentInParent<LootItem>();
                if (next != null && next.IsHeld.Value)
                    next = null;
            }

            if (_lookTarget == next)
                return;

            if (_lookTarget != null)
                _lookTarget.SetHighlight(false);

            _lookTarget = next;

            if (_lookTarget != null)
                _lookTarget.SetHighlight(true);
        }

        void ClearHighlight()
        {
            if (_lookTarget != null)
            {
                _lookTarget.SetHighlight(false);
                _lookTarget = null;
            }
        }

        void HandleInput()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null)
                return;

            if (kb.eKey.wasPressedThisFrame)
            {
                if (IsHolding)
                    RequestDropServerRpc();
                else if (_lookTarget != null)
                    RequestPickupServerRpc(_lookTarget.NetworkObject);
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame && IsHolding)
            {
                Vector3 dir = rayOrigin != null ? rayOrigin.forward : transform.forward;
                RequestThrowServerRpc(dir);
            }
        }

        [ServerRpc]
        void RequestPickupServerRpc(NetworkObjectReference lootRef, ServerRpcParams rpcParams = default)
        {
            if (_serverHeld != null)
                return;

            if (!lootRef.TryGet(out NetworkObject lootNet) ||
                !lootNet.TryGetComponent(out LootItem loot))
                return;

            if (!IsWithinReach(loot.transform.position))
                return;

            if (holdPoint == null)
                return;

            // First ServerRpc processed for this loot wins; others get false.
            if (!loot.ServerTryBeginHold(holdPoint, OwnerClientId))
                return;

            _serverHeld = loot;
            _heldLootRef.Value = loot.NetworkObject;
        }

        [ServerRpc]
        void RequestDropServerRpc()
        {
            ServerReleaseHeld(Vector3.zero);
        }

        [ServerRpc]
        void RequestThrowServerRpc(Vector3 throwDirection)
        {
            if (_serverHeld == null)
                return;

            Vector3 dir = throwDirection.sqrMagnitude > 0.001f
                ? throwDirection.normalized
                : transform.forward;

            float impulse = throwForce / Mathf.Max(0.5f, _serverHeld.Weight);
            ServerReleaseHeld(dir * impulse);
        }

        void ServerReleaseHeld(Vector3 throwImpulse)
        {
            if (!IsServer || _serverHeld == null)
                return;

            var loot = _serverHeld;
            _serverHeld = null;
            _heldLootRef.Value = default;
            loot.ServerTryEndHold(OwnerClientId, throwImpulse);
        }

        bool IsWithinReach(Vector3 worldPos)
        {
            Vector3 from = holdPoint != null ? holdPoint.position : transform.position;
            return Vector3.Distance(from, worldPos) <= serverMaxDistance;
        }
    }
}
