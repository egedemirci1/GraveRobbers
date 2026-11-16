using System.Collections.Generic;
using System.Reflection;
using GraveRobbers.Interaction;
using UnityEngine;

namespace GraveRobbers.Player
{
    /// <summary>
    /// Plays Idle, Walk, and Run on the character's own humanoid avatar.
    /// The old single walk clip is only a fallback when the locomotion controller is missing.
    /// </summary>
    public class PlayerWalkAnimation : MonoBehaviour
    {
        const float StartSpeed = 0.4f;
        const float StopSpeed = 0.15f;
        const float NaturalWalkSpeed = 2.2f;
        const float MaxPlayback = 2.4f;
        const float TeleportDistance = 3f;
        const float SprintSpeedThreshold = 6f;
        const string SpeedParam = "Speed";
        const string SprintParam = "Sprint";
        const string GroundedParam = "Grounded";
        const string CarryParam = "Carry";

        [SerializeField] RuntimeAnimatorController controller;
        [SerializeField] RuntimeAnimatorController locomotionController;
        [SerializeField] Avatar[] avatars;
        [SerializeField] GameObject poseSource;

        Animator _animator;
        PickupSystem _pickup;
        Transform[] _bones;
        Vector3[] _localPositions;
        Quaternion[] _localRotations;
        Vector3[] _localScales;
        Vector3 _lastPosition;
        float _commandedSpeed = -1f;
        bool _commandedSprint;
        bool _commandedGrounded = true;
        bool _ready;
        bool _primed;
        bool _walking;
        bool _mixamo;
        bool _inAir;
        float _airTime;

        public bool Bind(GameObject visual, int avatarIndex)
        {
            _bones = visual.GetComponentsInChildren<Transform>(true);
            _localPositions = new Vector3[_bones.Length];
            _localRotations = new Quaternion[_bones.Length];
            _localScales = new Vector3[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                _localPositions[i] = _bones[i].localPosition;
                _localRotations[i] = _bones[i].localRotation;
                _localScales[i] = _bones[i].localScale;
            }

            _animator = visual.GetComponent<Animator>();
            if (_animator == null || _animator.avatar == null)
            {
                Animator nested = visual.GetComponentInChildren<Animator>();
                if (nested != null)
                    _animator = nested;
            }

            if (_animator == null)
                _animator = visual.AddComponent<Animator>();

            if (locomotionController == null)
                locomotionController = Resources.Load<RuntimeAnimatorController>("PlayerLocomotion");

            if (locomotionController != null)
                return BindLocomotion(visual);

            _animator.enabled = false;
            if (controller == null)
                controller = Resources.Load<RuntimeAnimatorController>("PlayerWalk");

            Avatar built = BuildAvatar(visual);
            if (built != null && built.isValid)
                _animator.avatar = built;
            else if (avatars != null && avatarIndex >= 0 && avatarIndex < avatars.Length)
                _animator.avatar = avatars[avatarIndex];

            if (controller != null)
                _animator.runtimeAnimatorController = controller;

            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.updateMode = AnimatorUpdateMode.Normal;
            _animator.enabled = true;
            _animator.Rebind();
            _animator.Update(0f);

            SkinnedMeshRenderer[] meshes = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < meshes.Length; i++)
                meshes[i].updateWhenOffscreen = true;

            bool hips = _animator.GetBoneTransform(HumanBodyBones.Hips) != null;
            _animator.enabled = false;
            RestorePose();

            _mixamo = false;
            _walking = false;
            _primed = false;
            _commandedSpeed = -1f;
            _commandedSprint = false;
            _ready = hips && _animator.runtimeAnimatorController != null;
            if (!_ready)
            {
                Debug.LogWarning(
                    "Walk clip could not bind to this character rig. avatar=" +
                    (_animator.avatar != null) + " valid=" + (_animator.avatar != null && _animator.avatar.isValid) +
                    " hips=" + hips,
                    visual);
            }

            return _ready;
        }

        public void SetPlanarSpeed(float metersPerSecond)
        {
            SetLocomotion(metersPerSecond, false, true);
        }

        public void SetLocomotion(float metersPerSecond, bool sprint)
        {
            SetLocomotion(metersPerSecond, sprint, true);
        }

        public void SetLocomotion(float metersPerSecond, bool sprint, bool grounded)
        {
            // Sticky until the next command. Network proxies only get updates when
            // a NetworkVariable changes, so one-shot consume would drop to idle.
            _commandedSpeed = Mathf.Max(0f, metersPerSecond);
            _commandedSprint = sprint;
            _commandedGrounded = grounded;
        }

        bool BindLocomotion(GameObject visual)
        {
            // The locomotion clips were baked against the Mixamo model's avatar.
            // A Floreswa humanoid avatar is valid but its muscle zero is a T-pose,
            // so the idle clip (near zero) leaves that character stuck in a T-pose.
            Avatar sourceAvatar = PoseSourceAvatar();
            Avatar avatar = _animator.avatar;
            bool clipAvatar = sourceAvatar != null && avatar == sourceAvatar && avatar.isValid && avatar.isHuman;
            if (!clipAvatar)
                avatar = BuildMatchedAvatar(visual);

            if (avatar != null)
                _animator.avatar = avatar;

            _animator.runtimeAnimatorController = locomotionController;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.updateMode = AnimatorUpdateMode.Normal;
            _animator.SetBool(GroundedParam, true);
            _animator.enabled = true;
            _animator.Rebind();
            _animator.Update(0f);

            SkinnedMeshRenderer[] meshes = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < meshes.Length; i++)
                meshes[i].updateWhenOffscreen = true;

            _mixamo = true;
            _walking = false;
            _primed = false;
            _commandedSpeed = -1f;
            _commandedSprint = false;
            _commandedGrounded = true;
            if (_animator != null)
                _animator.SetBool(GroundedParam, true);
            _ready = avatar != null && avatar.isValid && avatar.isHuman;
            if (!_ready)
            {
                Debug.LogWarning("Locomotion could not bind to this character avatar.", visual);
            }

            return _ready;
        }

        void Update()
        {
            if (!_ready)
                return;

            if (!_primed)
            {
                _lastPosition = transform.position;
                _primed = true;
                return;
            }

            float speed;
            bool sprint;
            bool grounded = _commandedGrounded;
            if (_commandedSpeed >= 0f)
            {
                speed = _commandedSpeed;
                sprint = _commandedSprint;
                grounded = _commandedGrounded;
                _lastPosition = transform.position;
            }
            else
            {
                Vector3 delta = transform.position - _lastPosition;
                _lastPosition = transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > TeleportDistance * TeleportDistance)
                    return;

                speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
                sprint = speed > SprintSpeedThreshold;
            }

            if (_mixamo)
            {
                PlayMixamo(speed, sprint, grounded);
                return;
            }

            bool walk = _walking ? speed > StopSpeed : speed > StartSpeed;
            if (!walk)
            {
                if (_walking)
                    StopWalk();
                return;
            }

            if (!_walking)
            {
                _animator.enabled = true;
                _animator.Rebind();
                _animator.Play("Walk", 0, 0f);
                _animator.Update(0f);
                _walking = true;
            }

            _animator.speed = Mathf.Clamp(speed / NaturalWalkSpeed, 0.8f, MaxPlayback);
        }

        void PlayMixamo(float speed, bool sprint, bool grounded)
        {
            if (_pickup == null)
                _pickup = GetComponent<PickupSystem>();

            bool running = sprint && speed > StopSpeed;
            _animator.SetFloat(SpeedParam, speed);
            _animator.SetBool(SprintParam, running);
            _animator.SetBool(GroundedParam, grounded);
            _animator.SetBool(CarryParam, _pickup != null && _pickup.IsHolding);

            if (!grounded)
            {
                // The clip also contains the crouch and the landing. Playing all of
                // that during the short hop, sped up, is what made the jump look wrong.
                // Start in the airborne pose and hold it until the capsule lands.
                if (!_inAir)
                {
                    _animator.Play("Jump", 0, 0.34f);
                    _inAir = true;
                    _airTime = 0f;
                }

                _airTime += Time.deltaTime;
                AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
                _animator.speed = info.IsName("Jump") && (info.normalizedTime >= 0.46f || _airTime > 0.22f) ? 0f : 1f;
                return;
            }

            _inAir = false;
            _airTime = 0f;
            if (!running && speed <= StopSpeed)
                _animator.speed = 1f;
            else if (running)
                _animator.speed = Mathf.Clamp(speed / 4.5f, 0.85f, 1.4f);
            else
                _animator.speed = Mathf.Clamp(speed / NaturalWalkSpeed, 0.85f, 1.7f);
        }

        Avatar PoseSourceAvatar()
        {
            if (poseSource == null)
                return null;

            Animator animator = poseSource.GetComponent<Animator>();
            if (animator == null)
                animator = poseSource.GetComponentInChildren<Animator>(true);
            return animator != null ? animator.avatar : null;
        }

        void StopWalk()
        {
            _walking = false;
            if (_animator != null)
            {
                _animator.speed = 1f;
                _animator.enabled = false;
            }

            RestorePose();
        }

        static readonly string[] BoneMap =
        {
            "spine", "Hips",
            "spine.001", "Spine",
            "spine.002", "Chest",
            "spine.003", "UpperChest",
            "spine.004", "Neck",
            "spine.005", "Head",
            "thigh.L", "LeftUpperLeg",
            "shin.L", "LeftLowerLeg",
            "foot.L", "LeftFoot",
            "toe.L", "LeftToes",
            "thigh.R", "RightUpperLeg",
            "shin.R", "RightLowerLeg",
            "foot.R", "RightFoot",
            "toe.R", "RightToes",
            "shoulder.L", "LeftShoulder",
            "upper_arm.L", "LeftUpperArm",
            "forearm.L", "LeftLowerArm",
            "hand.L", "LeftHand",
            "shoulder.R", "RightShoulder",
            "upper_arm.R", "RightUpperArm",
            "forearm.R", "RightLowerArm",
            "hand.R", "RightHand"
        };

        Avatar BuildMatchedAvatar(GameObject visual)
        {
            if (poseSource == null)
                return null;

            GameObject source = Instantiate(poseSource);
            source.SetActive(false);
            try
            {
                MatchPose(visual.transform, source.transform);
                return BuildAvatar(visual, applyTPose: false);
            }
            finally
            {
                Destroy(source);
            }
        }

        static void MatchPose(Transform targetRoot, Transform sourceRoot)
        {
            string[][] chains =
            {
                new[] { "spine", "spine.001", "spine.002", "spine.003", "spine.004", "spine.005", "spine.006" },
                new[] { "shoulder.L", "upper_arm.L", "forearm.L", "hand.L", "hand.L_end" },
                new[] { "shoulder.R", "upper_arm.R", "forearm.R", "hand.R", "hand.R_end" },
                new[] { "thigh.L", "shin.L", "foot.L", "toe.L", "toe.L_end" },
                new[] { "thigh.R", "shin.R", "foot.R", "toe.R", "toe.R_end" }
            };

            for (int c = 0; c < chains.Length; c++)
            {
                string[] chain = chains[c];
                for (int i = 0; i < chain.Length - 1; i++)
                    MatchBone(targetRoot, sourceRoot, chain[i], chain[i + 1]);
            }
        }

        static void MatchBone(Transform targetRoot, Transform sourceRoot, string boneName, string childName)
        {
            Transform sourceBone = FindNamed(sourceRoot, boneName);
            Transform sourceChild = FindNamed(sourceRoot, childName);
            Transform targetBone = FindNamed(targetRoot, boneName);
            Transform targetChild = FindNamed(targetRoot, childName);
            if (sourceBone == null || sourceChild == null || targetBone == null || targetChild == null)
                return;

            Vector3 axis = sourceChild.position - sourceBone.position;
            if (axis.sqrMagnitude < 0.000001f)
                return;

            Vector3 secondary = sourceBone.forward;
            if (Mathf.Abs(Vector3.Dot(secondary.normalized, axis.normalized)) > 0.9f)
                secondary = sourceBone.right;

            Vector3 axisLocal = sourceRoot.InverseTransformDirection(axis);
            Vector3 secondaryLocal = sourceRoot.InverseTransformDirection(secondary);
            PointBone(
                targetBone,
                targetChild,
                targetRoot.TransformDirection(axisLocal),
                targetRoot.TransformDirection(secondaryLocal));
        }

        static Avatar BuildAvatar(GameObject visual, bool applyTPose = true)
        {
            if (applyTPose)
                ApplyTPose(visual.transform);
            var description = new HumanDescription
            {
                human = BuildHumanBones(visual.transform),
                skeleton = BuildSkeleton(visual.transform),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f
            };

            Avatar avatar = AvatarBuilder.BuildHumanAvatar(visual, description);
            if (avatar == null || !avatar.isValid)
            {
                if (avatar != null)
                    Object.Destroy(avatar);
                return null;
            }

            avatar.name = visual.name + "_Avatar";
            return avatar;
        }

        static void ApplyTPose(Transform root)
        {
            Vector3 up = root.up;
            Vector3 forward = root.forward;
            Vector3 left = -root.right;
            Vector3 right = root.right;
            Vector3 down = -up;

            PointChain(root, new[] { "spine", "spine.001", "spine.002", "spine.003", "spine.004", "spine.005", "spine.006" }, up);

            Vector3 leftReach = (left + forward * 0.12f).normalized;
            Vector3 rightReach = (right + forward * 0.12f).normalized;
            PointBone(FindNamed(root, "shoulder.L"), FindNamed(root, "upper_arm.L"), left, up);
            PointBone(FindNamed(root, "upper_arm.L"), FindNamed(root, "forearm.L"), left, up);
            PointBone(FindNamed(root, "forearm.L"), FindNamed(root, "hand.L"), leftReach, up);
            PointBone(FindNamed(root, "hand.L"), FindNamed(root, "hand.L_end"), leftReach, up);

            PointBone(FindNamed(root, "shoulder.R"), FindNamed(root, "upper_arm.R"), right, up);
            PointBone(FindNamed(root, "upper_arm.R"), FindNamed(root, "forearm.R"), right, up);
            PointBone(FindNamed(root, "forearm.R"), FindNamed(root, "hand.R"), rightReach, up);
            PointBone(FindNamed(root, "hand.R"), FindNamed(root, "hand.R_end"), rightReach, up);

            // Aim only lines the bone up with its child. Rigify leaves the toe rolled
            // about 180 degrees off the foot, so the humanoid T-pose keeps that twist
            // and the walk cycle rolls the left ankle. The hint puts the knee forward
            // and the top of the foot up after the aim.
            PointBone(FindNamed(root, "thigh.L"), FindNamed(root, "shin.L"), (down + left * 0.06f).normalized, forward);
            PointBone(FindNamed(root, "shin.L"), FindNamed(root, "foot.L"), (down + forward * 0.18f).normalized, forward);
            PointBone(FindNamed(root, "foot.L"), FindNamed(root, "toe.L"), forward, up);
            PointBone(FindNamed(root, "toe.L"), FindNamed(root, "toe.L_end"), forward, up);

            PointBone(FindNamed(root, "thigh.R"), FindNamed(root, "shin.R"), (down + right * 0.06f).normalized, forward);
            PointBone(FindNamed(root, "shin.R"), FindNamed(root, "foot.R"), (down + forward * 0.18f).normalized, forward);
            PointBone(FindNamed(root, "foot.R"), FindNamed(root, "toe.R"), forward, up);
            PointBone(FindNamed(root, "toe.R"), FindNamed(root, "toe.R_end"), forward, up);
        }

        static void PointChain(Transform root, string[] names, Vector3 direction)
        {
            for (int i = 0; i < names.Length - 1; i++)
                PointBone(FindNamed(root, names[i]), FindNamed(root, names[i + 1]), direction);
        }

        static void PointBone(Transform bone, Transform child, Vector3 desiredDirection)
        {
            PointBone(bone, child, desiredDirection, Vector3.zero);
        }

        static void PointBone(Transform bone, Transform child, Vector3 desiredDirection, Vector3 twistHint)
        {
            if (bone == null || child == null)
                return;

            Vector3 current = child.position - bone.position;
            if (current.sqrMagnitude < 0.000001f || desiredDirection.sqrMagnitude < 0.000001f)
                return;

            Vector3 reference = Vector3.ProjectOnPlane(twistHint, current);
            bool hasTwist = reference.sqrMagnitude > 0.000001f;
            Vector3 localReference = Vector3.zero;
            if (hasTwist)
                localReference = Quaternion.Inverse(bone.rotation) * reference.normalized;

            bone.rotation = Quaternion.FromToRotation(current, desiredDirection) * bone.rotation;
            if (!hasTwist)
                return;

            Vector3 axis = child.position - bone.position;
            Vector3 have = Vector3.ProjectOnPlane(bone.rotation * localReference, axis);
            Vector3 want = Vector3.ProjectOnPlane(twistHint, axis);
            if (have.sqrMagnitude < 0.000001f || want.sqrMagnitude < 0.000001f)
                return;

            bone.rotation = Quaternion.FromToRotation(have, want) * bone.rotation;
        }

        static HumanBone[] BuildHumanBones(Transform root)
        {
            var bones = new List<HumanBone>();
            for (int i = 0; i < BoneMap.Length; i += 2)
            {
                if (FindNamed(root, BoneMap[i]) == null)
                    continue;

                var bone = new HumanBone
                {
                    boneName = BoneMap[i],
                    humanName = BoneMap[i + 1]
                };
                bone.limit.useDefaultValues = true;
                bones.Add(bone);
            }

            return bones.ToArray();
        }

        static SkeletonBone[] BuildSkeleton(Transform root)
        {
            FieldInfo parentField = typeof(SkeletonBone).GetField(
                "parentName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            var bones = new SkeletonBone[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                var bone = new SkeletonBone
                {
                    name = current.name,
                    position = current.localPosition,
                    rotation = current.localRotation,
                    scale = current.localScale
                };

                if (parentField != null)
                {
                    object boxed = bone;
                    parentField.SetValue(boxed, current == root ? "" : current.parent.name);
                    bone = (SkeletonBone)boxed;
                }

                bones[i] = bone;
            }

            return bones;
        }

        static Transform FindNamed(Transform root, string boneName)
        {
            if (root.name == boneName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindNamed(root.GetChild(i), boneName);
                if (found != null)
                    return found;
            }

            return null;
        }

        void RestorePose()
        {
            if (_bones == null)
                return;

            for (int i = 0; i < _bones.Length; i++)
            {
                Transform bone = _bones[i];
                if (bone == null)
                    continue;

                bone.localPosition = _localPositions[i];
                bone.localRotation = _localRotations[i];
                bone.localScale = _localScales[i];
            }
        }
    }
}
