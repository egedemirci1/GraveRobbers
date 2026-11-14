using GraveRobbers.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GraveRobbers.Player
{
    /// <summary>
    /// Owner-only movement / look. Proxies are driven by ClientNetworkTransform.
    /// Body tint follows OwnerClientId (up to 4 distinct colors).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        static readonly Color[] ClientColors =
        {
            new Color(0.35f, 0.65f, 1.00f), // 0 blue
            new Color(1.00f, 0.50f, 0.25f), // 1 orange
            new Color(0.40f, 0.85f, 0.45f), // 2 green
            new Color(0.85f, 0.40f, 0.90f), // 3 purple
        };

        [Header("References")]
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] Transform cameraTransform;
        [SerializeField] MeshRenderer bodyRenderer;
        [SerializeField] GameObject[] characterPrefabs;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 4.5f;
        [SerializeField] float sprintSpeed = 7.5f;
        [SerializeField] float jumpHeight = 1.2f;
        [SerializeField] float gravity = -20f;
        [SerializeField] float groundedStickForce = -2f;

        [Header("Look")]
        [SerializeField] float lookSensitivity = 0.15f;
        [SerializeField] float minPitch = -85f;
        [SerializeField] float maxPitch = 85f;
        [SerializeField] bool lockCursor = true;

        readonly NetworkVariable<int> _spawnIndex = new(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        readonly NetworkVariable<float> _animSpeed = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> _animSprint = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<bool> _animGrounded = new(
            true,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        CharacterController _controller;
        PlayerStamina _stamina;
        PlayerWalkAnimation _walkAnimation;
        Camera _camera;
        AudioListener _audioListener;
        InputActionMap _playerMap;
        InputAction _moveAction;
        InputAction _lookAction;
        InputAction _jumpAction;
        InputAction _sprintAction;

        Vector3 _velocity;
        float _pitch;
        bool _jumpQueued;
        bool _localReady;
        bool _wantCursorLock = true;
        bool _sprinting;
        Transform _visual;
        Transform _headBone;
        float _restHeadForward;

        public float SpeedMultiplier { get; set; } = 1f;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _stamina = GetComponent<PlayerStamina>();
            _walkAnimation = GetComponent<PlayerWalkAnimation>();
            _camera = GetComponentInChildren<Camera>(true);
            _audioListener = GetComponentInChildren<AudioListener>(true);

            if (cameraTransform == null && _camera != null)
                cameraTransform = _camera.transform.parent != null
                    ? _camera.transform.parent
                    : _camera.transform;

            if (bodyRenderer == null)
            {
                var body = transform.Find("Body");
                if (body != null)
                    bodyRenderer = body.GetComponent<MeshRenderer>();
            }

            if (GetComponent<NetworkObject>() != null)
                SetLocalGearActive(false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _spawnIndex.OnValueChanged += OnSpawnIndexChanged;
            _animSpeed.OnValueChanged += OnAnimStateChanged;
            _animSprint.OnValueChanged += OnAnimStateChanged;
            _animGrounded.OnValueChanged += OnAnimStateChanged;
            AttachCharacterVisual();
            ApplyNetworkAnim();

            if (IsServer)
            {
                SpawnPoint point = SpawnPoint.ServerClaim(OwnerClientId);
                if (point != null)
                {
                    _spawnIndex.Value = point.Index;
                    ApplySpawnTransform(point.transform);
                }
            }
            else if (_spawnIndex.Value >= 0)
            {
                ApplySpawnByIndex(_spawnIndex.Value);
            }

            if (IsOwner)
            {
                SetLocalGearActive(true);
                EnableInput();
                _localReady = true;

                _wantCursorLock = lockCursor;
                ApplyCursorState();
            }
            else
            {
                SetLocalGearActive(false);
                if (_controller != null)
                    _controller.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            _spawnIndex.OnValueChanged -= OnSpawnIndexChanged;
            _animSpeed.OnValueChanged -= OnAnimStateChanged;
            _animSprint.OnValueChanged -= OnAnimStateChanged;
            _animGrounded.OnValueChanged -= OnAnimStateChanged;

            if (IsServer)
                SpawnPoint.ServerRelease(OwnerClientId);

            DisableInput();
            _localReady = false;

            if (IsOwner && lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            base.OnNetworkDespawn();
        }

        void OnSpawnIndexChanged(int previous, int current)
        {
            if (current >= 0)
                ApplySpawnByIndex(current);
        }

        void OnDisable()
        {
            DisableInput();
        }

        void SetLocalGearActive(bool active)
        {
            if (_camera != null)
            {
                _camera.enabled = active;
                if (active)
                    _camera.tag = "MainCamera";
                else if (_camera.CompareTag("MainCamera"))
                    _camera.tag = "Untagged";
            }

            if (_audioListener != null)
                _audioListener.enabled = active;
        }

        void AttachCharacterVisual()
        {
            Transform capsule = transform.Find("Body");
            if (characterPrefabs == null || characterPrefabs.Length == 0)
            {
                if (capsule != null)
                    capsule.gameObject.SetActive(true);
                ApplyBodyTint();
                return;
            }

            if (capsule != null)
                capsule.gameObject.SetActive(false);

            int index = (int)(OwnerClientId % (ulong)characterPrefabs.Length);
            GameObject prefab = characterPrefabs[index];
            if (prefab == null)
                return;

            Transform existing = transform.Find("Character");
            if (existing != null)
                Destroy(existing.gameObject);

            GameObject visual = Instantiate(prefab, transform);
            visual.name = "Character";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            if (_walkAnimation == null)
                _walkAnimation = GetComponent<PlayerWalkAnimation>();
            _walkAnimation?.Bind(visual, index);

            CacheVisual(visual.transform);
            if (IsOwner)
                FollowEyes();
        }

        void CacheVisual(Transform visual)
        {
            _visual = visual;
            _headBone = FindNamed(visual, "spine.006");
            if (_headBone == null)
                _headBone = FindHead(visual);

            if (_headBone != null)
                _restHeadForward = Vector3.Dot(_headBone.position - visual.position, FlatForward());
        }

        void LateUpdate()
        {
            KeepHeadBehindCamera();
            if (IsOwner && _localReady)
                FollowEyes();
        }

        void OnAnimStateChanged(float previous, float current) => ApplyNetworkAnim();

        void OnAnimStateChanged(bool previous, bool current) => ApplyNetworkAnim();

        void ApplyNetworkAnim()
        {
            if (_walkAnimation == null)
                _walkAnimation = GetComponent<PlayerWalkAnimation>();
            _walkAnimation?.SetLocomotion(_animSpeed.Value, _animSprint.Value, _animGrounded.Value);
        }

        void FollowEyes()
        {
            if (cameraTransform == null)
                return;

            if (_headBone == null)
            {
                cameraTransform.position = transform.position + Vector3.up * 1.65f;
                return;
            }

            Transform crown = FindNamed(_headBone, "spine.006_end");
            Vector3 up = crown != null ? crown.position - _headBone.position : transform.up;
            if (up.sqrMagnitude < 0.0001f)
                up = transform.up;

            // Track the animated head so jump and lean move the view with the body.
            Vector3 eyes = _headBone.position + up.normalized * (up.magnitude * 0.28f);
            cameraTransform.position = eyes + FlatForward() * 0.3f;
        }

        void KeepHeadBehindCamera()
        {
            // Mixamo's run leans the chest forward of the root. Pull the visual
            // back on every client so remotes see the same corrected pose.
            if (_visual == null || _headBone == null)
                return;

            Vector3 forward = FlatForward();
            float posed = Vector3.Dot(_headBone.position - _visual.position, forward);
            float excess = posed - _restHeadForward;
            Vector3 local = _visual.localPosition;
            bool hold = _animSprint.Value || _sprinting || local.z < -0.001f;
            float pull = 0f;
            if (hold && excess > 0.02f)
                pull = excess + 0.06f * Mathf.Clamp01(excess / 0.1f);

            local.x = 0f;
            local.y = 0f;
            local.z = -pull;
            _visual.localPosition = local;
        }

        Vector3 FlatForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, transform.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;
            return forward.normalized;
        }

        static Transform FindNamed(Transform root, string name)
        {
            if (root.name == name)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindNamed(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        static Transform FindHead(Transform root)
        {
            if (root.name == "Head" || root.name.EndsWith(":Head"))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindHead(root.GetChild(i));
                if (found != null)
                    return found;
            }

            return null;
        }

        void ApplyBodyTint()
        {
            if (bodyRenderer == null)
                return;

            int colorIndex = (int)(OwnerClientId % (ulong)ClientColors.Length);
            Color color = ClientColors[colorIndex];

            var mat = bodyRenderer.material;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;
        }

        void ApplySpawnByIndex(int index)
        {
            var points = FindObjectsByType<SpawnPoint>();
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].Index == index)
                {
                    ApplySpawnTransform(points[i].transform);
                    return;
                }
            }
        }

        void ApplySpawnTransform(Transform point)
        {
            bool wasEnabled = _controller != null && _controller.enabled;
            if (_controller != null)
                _controller.enabled = false;

            transform.SetPositionAndRotation(point.position, point.rotation);

            if (_controller != null)
                _controller.enabled = wasEnabled || IsOwner;
        }

        void EnableInput()
        {
            if (inputActions == null)
            {
                Debug.LogError("PlayerController: InputActionAsset is missing.", this);
                return;
            }

            if (_playerMap != null)
                return;

            _playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
            _moveAction = _playerMap.FindAction("Move", throwIfNotFound: true);
            _lookAction = _playerMap.FindAction("Look", throwIfNotFound: true);
            _jumpAction = _playerMap.FindAction("Jump", throwIfNotFound: true);
            _sprintAction = _playerMap.FindAction("Sprint", throwIfNotFound: true);

            _jumpAction.performed += OnJumpPerformed;
            _playerMap.Enable();
        }

        void DisableInput()
        {
            if (_jumpAction != null)
                _jumpAction.performed -= OnJumpPerformed;

            _playerMap?.Disable();
            _playerMap = null;
            _moveAction = null;
            _lookAction = null;
            _jumpAction = null;
            _sprintAction = null;
        }

        void OnJumpPerformed(InputAction.CallbackContext _)
        {
            if (_localReady)
                _jumpQueued = true;
        }

        void Update()
        {
            if (!_localReady || !IsOwner)
                return;

            // Esc bakışı kapatmaz; cursor oyun sırasında kilitli kalır.
            _wantCursorLock = lockCursor;
            ApplyCursorState();
            HandleLook();
            HandleMove();
        }

        void ApplyCursorState()
        {
            if (!lockCursor)
                return;

            if (_wantCursorLock)
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                    Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void HandleLook()
        {
            if (cameraTransform == null)
                return;

            // Esc ile bilinçli açıldıysa bakışı durdur; kilit başarısız olsa bile bakış çalışsın.
            if (lockCursor && !_wantCursorLock)
                return;

            Vector2 look = Vector2.zero;

            // Editor / MPPM'de Input Action bazen 0 gelir — mouse delta daha güvenilir.
            if (Mouse.current != null)
                look = Mouse.current.delta.ReadValue();

            if (look.sqrMagnitude < 0.0001f && _lookAction != null)
                look = _lookAction.ReadValue<Vector2>();

            if (look.sqrMagnitude < 0.0001f)
                return;

            float yaw = look.x * lookSensitivity;
            float pitchDelta = look.y * lookSensitivity;

            transform.Rotate(0f, yaw, 0f);

            _pitch = Mathf.Clamp(_pitch - pitchDelta, minPitch, maxPitch);
            cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void HandleMove()
        {
            bool grounded = _controller.isGrounded;
            if (grounded && _velocity.y < 0f)
                _velocity.y = groundedStickForce;

            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
            if (move.sqrMagnitude > 1f)
                move.Normalize();

            bool wantsSprint = _sprintAction.IsPressed() && move.sqrMagnitude > 0.01f;
            if (_stamina != null)
            {
                _stamina.TickSprint(wantsSprint && _stamina.CanSprint);
                wantsSprint = wantsSprint && _stamina.CanSprint;
            }

            float speed = (wantsSprint ? sprintSpeed : walkSpeed) * SpeedMultiplier;
            _controller.Move(move * (speed * Time.deltaTime));
            bool moving = move.sqrMagnitude > 0.01f;
            _sprinting = wantsSprint && moving;

            if (_jumpQueued && grounded)
                _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _jumpQueued = false;
            _velocity.y += gravity * Time.deltaTime;
            _controller.Move(_velocity * Time.deltaTime);

            bool onGround = _controller.isGrounded && _velocity.y <= 0f;
            float planarSpeed = move.magnitude * speed;
            _animSpeed.Value = planarSpeed;
            _animSprint.Value = _sprinting;
            _animGrounded.Value = onGround;
            if (_walkAnimation == null)
                _walkAnimation = GetComponent<PlayerWalkAnimation>();
            _walkAnimation?.SetLocomotion(planarSpeed, _sprinting, onGround);
        }
    }
}
