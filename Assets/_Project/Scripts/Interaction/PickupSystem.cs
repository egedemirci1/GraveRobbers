using GraveRobbers.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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

        static readonly Color CrosshairGlow = new Color(1f, 0.78f, 0.25f, 1f);
        static Sprite _dotSprite;

        LootItem _lookTarget;
        LootItem _serverHeld;
        GameObject _hudRoot;
        Image _crosshairDot;
        Image _crosshairGlow;
        RectTransform _crosshairDotRect;
        RectTransform _crosshairGlowRect;
        bool _crosshairOnLoot;

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

            if (IsOwner)
                EnsureHud();

            RefreshCarrySpeed();
        }

        public override void OnNetworkDespawn()
        {
            _heldLootRef.OnValueChanged -= OnHeldChanged;

            // Only this player's loot — other players' holds are untouched.
            if (IsServer && _serverHeld != null)
                ServerReleaseHeld(Vector3.zero);

            if (IsOwner)
            {
                ClearHighlight();
                if (_hudRoot != null)
                    Destroy(_hudRoot);
            }

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
            if (_crosshairOnLoot)
            {
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 2.2f);
                ApplyCrosshairVisual(pulse);
            }

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

            if (_hudRoot == null)
                EnsureHud();

            SetCrosshairLoot(_lookTarget != null);
        }

        void EnsureHud()
        {
            if (_hudRoot != null)
                return;

            var canvasGo = new GameObject("PlayerHud");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            CreateCrosshair(canvasGo.transform);

            _hudRoot = canvasGo;
            SetCrosshairLoot(false);
        }

        void CreateCrosshair(Transform parent)
        {
            var root = new GameObject("Crosshair");
            root.transform.SetParent(parent, false);
            var rootRect = root.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = Vector2.zero;

            _crosshairGlow = CreateDot(root.transform, "Glow", 28f);
            _crosshairGlowRect = _crosshairGlow.rectTransform;
            _crosshairDot = CreateDot(root.transform, "Dot", 7f);
            _crosshairDotRect = _crosshairDot.rectTransform;
        }

        Image CreateDot(Transform parent, string name, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);

            var image = go.AddComponent<Image>();
            image.sprite = DotSprite();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        static Sprite DotSprite()
        {
            if (_dotSprite != null)
                return _dotSprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _dotSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            _dotSprite.hideFlags = HideFlags.HideAndDontSave;
            return _dotSprite;
        }

        void SetCrosshairLoot(bool onLoot)
        {
            _crosshairOnLoot = onLoot;
            ApplyCrosshairVisual(onLoot ? 1f : 0f);
        }

        void ApplyCrosshairVisual(float glow)
        {
            if (_crosshairDot == null || _crosshairGlow == null)
                return;

            _crosshairDot.color = Color.Lerp(Color.white, new Color(1f, 0.96f, 0.8f, 1f), glow);
            _crosshairDotRect.sizeDelta = Vector2.one * Mathf.Lerp(7f, 9f, glow);

            var glowColor = CrosshairGlow;
            glowColor.a = Mathf.Lerp(0f, 0.95f, glow);
            _crosshairGlow.color = glowColor;
            _crosshairGlowRect.sizeDelta = Vector2.one * Mathf.Lerp(14f, 46f, glow);
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
