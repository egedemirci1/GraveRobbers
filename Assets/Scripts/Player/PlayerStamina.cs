using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GraveRobbers.Player
{
    /// <summary>
    /// Owner-only stamina. Sprint drains it while the player is actually moving.
    /// An empty bar stops sprint until a partial reserve returns.
    /// A camera-facing quad and a soft vignette ease in only as the bar runs out.
    /// </summary>
    public class PlayerStamina : NetworkBehaviour
    {
        static readonly int BlurIntensityId = Shader.PropertyToID("_BlurIntensity");

        [Header("Stamina")]
        [SerializeField] float maxStamina = 100f;
        [SerializeField] float drainPerSecond = 35f;
        [SerializeField] float regenPerSecond = 3.5f;
        [SerializeField] float regenDelay = 1.5f;
        [SerializeField] float resumeAt = 25f;

        [Header("Exhaustion look")]
        [SerializeField] float effectStartNormalized = 0.65f;
        [SerializeField] float effectFadeInSpeed = 2.2f;
        [SerializeField] float effectFadeOutSpeed = 0.22f;
        [SerializeField] float maxBlurIntensity = 1f;
        [SerializeField] float maxVignetteIntensity = 0.58f;
        [SerializeField] float vignetteSmoothness = 0.32f;

        float _stamina;
        float _regenDelayLeft;
        bool _exhausted;
        bool _drivesEffects;
        float _effect;
        Camera _camera;
        UniversalAdditionalCameraData _cameraData;
        GameObject _overlay;
        MeshRenderer _overlayRenderer;
        Material _blurMaterial;
        Volume _volume;
        VolumeProfile _profile;
        Vignette _vignette;
        GUIStyle _debugStyle;

        public float Normalized => maxStamina <= 0.01f ? 0f : _stamina / maxStamina;
        public bool CanSprint => !_exhausted && _stamina > 0.5f;

        void Awake()
        {
            _stamina = maxStamina;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            _drivesEffects = true;
            _stamina = maxStamina;
            EnsureOverlay();
            EnsureVignette();
            ApplyVisual(0f);
        }

        public override void OnNetworkDespawn()
        {
            if (_drivesEffects)
            {
                ApplyVisual(0f);
                _drivesEffects = false;
            }

            DestroyOverlay();
            DestroyVignette();
            base.OnNetworkDespawn();
        }

        void OnDisable()
        {
            if (_drivesEffects)
                ApplyVisual(0f);
        }

        void OnGUI()
        {
            if (!_drivesEffects)
                return;

            if (_debugStyle == null)
            {
                _debugStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold
                };
                _debugStyle.normal.textColor = Color.white;
            }

            float shown = Mathf.Clamp(Normalized * 100f, 0f, 100f);
            GUI.Label(new Rect(16f, 16f, 320f, 36f), $"Stamina {shown:0} / 100", _debugStyle);
        }

        /// <summary>
        /// Owner movement tick. Pass true only on a frame that actually sprints.
        /// </summary>
        public void TickSprint(bool sprinting)
        {
            if (_drivesEffects == false && IsSpawned && !IsOwner)
                return;

            if (sprinting && !_exhausted && _stamina > 0f)
            {
                _stamina = Mathf.Max(0f, _stamina - drainPerSecond * Time.deltaTime);
                _regenDelayLeft = regenDelay;
                if (_stamina <= 0.5f)
                {
                    _stamina = 0f;
                    _exhausted = true;
                }
            }
            else
            {
                if (_regenDelayLeft > 0f)
                    _regenDelayLeft -= Time.deltaTime;
                else
                    _stamina = Mathf.Min(maxStamina, _stamina + regenPerSecond * Time.deltaTime);

                if (_exhausted && _stamina >= resumeAt)
                    _exhausted = false;
            }

            UpdateEffects();
        }

        void UpdateEffects()
        {
            float target = 0f;
            float normalized = Normalized;
            if (normalized < effectStartNormalized)
            {
                float t = Mathf.InverseLerp(effectStartNormalized, 0f, normalized);
                target = t * t * (3f - 2f * t);
            }

            float fadeSpeed = target > _effect ? effectFadeInSpeed : effectFadeOutSpeed;
            _effect = Mathf.MoveTowards(_effect, target, fadeSpeed * Time.deltaTime);
            ApplyVisual(_effect);
        }

        void ApplyVisual(float amount)
        {
            bool visible = amount > 0.001f;

            if (_overlayRenderer != null)
            {
                _overlayRenderer.enabled = visible;
                if (visible)
                    FitOverlay();
            }

            if (_blurMaterial != null)
                _blurMaterial.SetFloat(BlurIntensityId, amount * maxBlurIntensity);

            if (_cameraData != null)
                _cameraData.renderPostProcessing = visible;

            if (_vignette != null)
                _vignette.intensity.value = visible ? amount * maxVignetteIntensity : 0f;
        }

        void EnsureOverlay()
        {
            if (_overlay != null)
                return;

            _camera = GetComponentInChildren<Camera>(true);
            if (_camera == null)
                return;

            _cameraData = _camera.GetComponent<UniversalAdditionalCameraData>();

            var shader = Shader.Find("GraveRobbers/StaminaEdgeBlur");
            if (shader == null)
            {
                Debug.LogError("PlayerStamina: StaminaEdgeBlur shader is missing.", this);
                return;
            }

            _blurMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "StaminaEdgeBlurRuntime"
            };
            _blurMaterial.SetFloat(BlurIntensityId, 0f);
            _blurMaterial.SetFloat("_InnerRadius", 0.62f);
            _blurMaterial.SetFloat("_OuterRadius", 1f);
            _blurMaterial.SetFloat("_SampleRadius", 0.008f);

            _overlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _overlay.name = "StaminaEdgeBlur";
            _overlay.hideFlags = HideFlags.HideAndDontSave;
            _overlay.transform.SetParent(_camera.transform, false);

            var collider = _overlay.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            _overlayRenderer = _overlay.GetComponent<MeshRenderer>();
            _overlayRenderer.sharedMaterial = _blurMaterial;
            _overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _overlayRenderer.receiveShadows = false;
            _overlayRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _overlayRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _overlayRenderer.enabled = false;
            FitOverlay();
        }

        void FitOverlay()
        {
            if (_camera == null || _overlay == null)
                return;

            float distance = Mathf.Max(0.2f, _camera.nearClipPlane + 0.08f);
            float height = 2f * distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float width = height * Mathf.Max(0.1f, _camera.aspect);
            _overlay.transform.localPosition = new Vector3(0f, 0f, distance);
            _overlay.transform.localRotation = Quaternion.identity;
            _overlay.transform.localScale = new Vector3(width * 1.05f, height * 1.05f, 1f);
        }

        void DestroyOverlay()
        {
            if (_overlay != null)
            {
                Destroy(_overlay);
                _overlay = null;
                _overlayRenderer = null;
            }

            if (_blurMaterial != null)
            {
                Destroy(_blurMaterial);
                _blurMaterial = null;
            }
        }

        void EnsureVignette()
        {
            if (_vignette != null)
                return;

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 20f;
            _volume.weight = 1f;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.hideFlags = HideFlags.HideAndDontSave;
            _volume.sharedProfile = _profile;

            var tonemap = _profile.Add<Tonemapping>(true);
            tonemap.mode.value = TonemappingMode.None;

            _vignette = _profile.Add<Vignette>(true);
            _vignette.color.value = Color.black;
            _vignette.center.value = new Vector2(0.5f, 0.5f);
            _vignette.intensity.value = 0f;
            _vignette.smoothness.value = vignetteSmoothness;
            _vignette.rounded.value = false;
        }

        void DestroyVignette()
        {
            _vignette = null;

            if (_volume != null)
            {
                _volume.sharedProfile = null;
                Destroy(_volume);
                _volume = null;
            }

            if (_profile != null)
            {
                Destroy(_profile);
                _profile = null;
            }
        }
    }
}
