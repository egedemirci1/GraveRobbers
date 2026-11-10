using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GraveRobbers.Player
{
    /// <summary>
    /// Plays the shared walk clip while the body is actually moving.
    /// Standing still restores the character's authored pose.
    /// </summary>
    public class PlayerWalkAnimation : MonoBehaviour
    {
        const float StartSpeed = 0.4f;
        const float StopSpeed = 0.15f;
        const float NaturalWalkSpeed = 2.2f;
        const float MaxPlayback = 2.4f;
        const float TeleportDistance = 3f;

        [SerializeField] RuntimeAnimatorController controller;
        [SerializeField] Avatar[] avatars;

        Animator _animator;
        Transform[] _bones;
        Vector3[] _localPositions;
        Quaternion[] _localRotations;
        Vector3[] _localScales;
        Vector3 _lastPosition;
        float _commandedSpeed = -1f;
        bool _ready;
        bool _primed;
        bool _walking;

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
            if (_animator == null)
                _animator = visual.AddComponent<Animator>();

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

            _walking = false;
            _primed = false;
            _commandedSpeed = -1f;
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
            _commandedSpeed = Mathf.Max(0f, metersPerSecond);
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
            if (_commandedSpeed >= 0f)
            {
                speed = _commandedSpeed;
                _commandedSpeed = -1f;
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

        static Avatar BuildAvatar(GameObject visual)
        {
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

            PointBone(FindNamed(root, "shoulder.L"), FindNamed(root, "upper_arm.L"), left);
            PointBone(FindNamed(root, "upper_arm.L"), FindNamed(root, "forearm.L"), left);
            PointBone(FindNamed(root, "forearm.L"), FindNamed(root, "hand.L"), (left + forward * 0.28f).normalized);
            PointBone(FindNamed(root, "hand.L"), FindNamed(root, "hand.L_end"), (left + forward * 0.28f).normalized);

            PointBone(FindNamed(root, "shoulder.R"), FindNamed(root, "upper_arm.R"), right);
            PointBone(FindNamed(root, "upper_arm.R"), FindNamed(root, "forearm.R"), right);
            PointBone(FindNamed(root, "forearm.R"), FindNamed(root, "hand.R"), (right + forward * 0.28f).normalized);
            PointBone(FindNamed(root, "hand.R"), FindNamed(root, "hand.R_end"), (right + forward * 0.28f).normalized);

            PointBone(FindNamed(root, "thigh.L"), FindNamed(root, "shin.L"), (down + left * 0.06f).normalized);
            PointBone(FindNamed(root, "shin.L"), FindNamed(root, "foot.L"), (down + forward * 0.18f).normalized);
            PointBone(FindNamed(root, "foot.L"), FindNamed(root, "toe.L"), forward);
            PointBone(FindNamed(root, "toe.L"), FindNamed(root, "toe.L_end"), forward);

            PointBone(FindNamed(root, "thigh.R"), FindNamed(root, "shin.R"), (down + right * 0.06f).normalized);
            PointBone(FindNamed(root, "shin.R"), FindNamed(root, "foot.R"), (down + forward * 0.18f).normalized);
            PointBone(FindNamed(root, "foot.R"), FindNamed(root, "toe.R"), forward);
            PointBone(FindNamed(root, "toe.R"), FindNamed(root, "toe.R_end"), forward);
        }

        static void PointChain(Transform root, string[] names, Vector3 direction)
        {
            for (int i = 0; i < names.Length - 1; i++)
                PointBone(FindNamed(root, names[i]), FindNamed(root, names[i + 1]), direction);
        }

        static void PointBone(Transform bone, Transform child, Vector3 desiredDirection)
        {
            if (bone == null || child == null)
                return;

            Vector3 current = child.position - bone.position;
            if (current.sqrMagnitude < 0.000001f || desiredDirection.sqrMagnitude < 0.000001f)
                return;

            bone.rotation = Quaternion.FromToRotation(current, desiredDirection) * bone.rotation;
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
