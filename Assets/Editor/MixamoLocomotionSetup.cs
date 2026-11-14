using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GraveRobbers.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GraveRobbers.Editor
{
    /// <summary>
    /// Imports the Mixamo idle (with skin) as male1 and retargets walk and run onto that avatar.
    /// Runs once from the editor menu, or automatically when Temp has the setup flag.
    /// </summary>
    public static class MixamoLocomotionSetup
    {
        const string IdlePath = "Assets/Idle.fbx";
        const string WalkPath = "Assets/Walking.fbx";
        const string RunPath = "Assets/Running.fbx";
        const string ControllerPath = "Assets/Resources/PlayerLocomotion.controller";
        const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";
        const string FlagName = "grave_robbers_mixamo_locomotion.flag";

        static bool _waitingForPlayMode;

        [InitializeOnLoadMethod]
        static void ScheduleRequestedSetup()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        [MenuItem("Grave Robbers/Setup Mixamo Locomotion")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Leave Play Mode, then run Grave Robbers/Setup Mixamo Locomotion.");
                return;
            }

            if (!File.Exists(IdlePath) || !File.Exists(WalkPath) || !File.Exists(RunPath))
            {
                Debug.LogError("Need Assets/Idle.fbx, Assets/Walking.fbx, and Assets/Running.fbx.");
                return;
            }

            Avatar avatar = FindAvatar(IdlePath);
            AnimationClip idle = FindClip(IdlePath);
            if (avatar == null || !avatar.isValid || !avatar.isHuman || idle == null)
            {
                ConfigureHuman(IdlePath);
                avatar = FindAvatar(IdlePath);
                idle = FindClip(IdlePath);
            }

            if (avatar == null || !avatar.isValid || !avatar.isHuman || idle == null)
            {
                Debug.LogError("Idle.fbx did not import as a humanoid character.");
                return;
            }

            // Walk and run were given avatars from their own first frame, so the
            // muscle axes do not match Idle. Rebuild those avatars on Idle's pose.
            ConfigureClipToIdlePose(WalkPath);
            ConfigureClipToIdlePose(RunPath);
            AnimationClip walk = FindClip(WalkPath);
            AnimationClip run = FindClip(RunPath);
            if (walk == null || run == null)
            {
                Debug.LogError("Walking.fbx or Running.fbx has no animation clip.");
                return;
            }

            RuntimeAnimatorController controller = CreateController(idle, walk, run);
            AssignPlayer(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("Idle, Walk, and Run play on the character's own model.");
        }

        static void RunIfRequested()
        {
            string flag = Path.Combine(Directory.GetCurrentDirectory(), "Temp", FlagName);
            if (!File.Exists(flag))
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunIfRequested;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (_waitingForPlayMode)
                    return;

                _waitingForPlayMode = true;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                return;
            }

            File.Delete(flag);
            Setup();
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
                return;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _waitingForPlayMode = false;
            EditorApplication.delayCall += RunIfRequested;
        }

        static void ConfigureHuman(string path)
        {
            // Motion-only Mixamo files have no skinned mesh. Without preserving the
            // hierarchy Unity strips the Rigify bones, so the Hips transform is missing.
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.preserveHierarchy = true;
            importer.importAnimation = true;
            HumanDescription cleared = importer.humanDescription;
            cleared.human = new HumanBone[0];
            cleared.skeleton = new SkeletonBone[0];
            importer.humanDescription = cleared;
            importer.SaveAndReimport();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool hasSpine = FindNamed(model.transform, "spine") != null;
            importer = (ModelImporter)AssetImporter.GetAtPath(path);
            HumanDescription description = importer.humanDescription;
            if (hasSpine)
            {
                description.human = RigifyMap();
                description.skeleton = BuildSkeleton(model);
                importer.autoGenerateAvatarMappingIfUnspecified = false;
            }
            else
            {
                description.human = new HumanBone[0];
                description.skeleton = new SkeletonBone[0];
                importer.autoGenerateAvatarMappingIfUnspecified = true;
                Debug.LogWarning(path + " has no spine bone after generic import.");
            }

            importer.humanDescription = description;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.preserveHierarchy = true;
            importer.importAnimation = true;
            ApplyClipSettings(importer);
            importer.SaveAndReimport();
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

        static void ConfigureClipToIdlePose(string path)
        {
            Dictionary<string, SkeletonBone> idlePose = IdlePose();
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.preserveHierarchy = true;
            importer.importAnimation = true;
            HumanDescription cleared = importer.humanDescription;
            cleared.human = new HumanBone[0];
            cleared.skeleton = new SkeletonBone[0];
            importer.humanDescription = cleared;
            importer.SaveAndReimport();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            importer = (ModelImporter)AssetImporter.GetAtPath(path);
            SkeletonBone[] skeleton = BuildSkeleton(model);
            for (int i = 0; i < skeleton.Length; i++)
            {
                if (!idlePose.TryGetValue(skeleton[i].name, out SkeletonBone source))
                    continue;

                SkeletonBone bone = skeleton[i];
                bone.position = source.position;
                bone.rotation = source.rotation;
                bone.scale = source.scale;
                skeleton[i] = bone;
            }

            HumanDescription description = importer.humanDescription;
            description.human = RigifyMap();
            description.skeleton = skeleton;
            importer.humanDescription = description;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.autoGenerateAvatarMappingIfUnspecified = false;
            importer.preserveHierarchy = true;
            importer.importAnimation = true;
            ApplyClipSettings(importer);
            importer.SaveAndReimport();
        }

        static Dictionary<string, SkeletonBone> IdlePose()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(IdlePath);
            var pose = new Dictionary<string, SkeletonBone>();
            SkeletonBone[] bones = importer.humanDescription.skeleton;
            for (int i = 0; i < bones.Length; i++)
                pose[bones[i].name] = bones[i];
            return pose;
        }

        static HumanBone[] RigifyMap()
        {
            return new[]
            {
                Bone("spine", "Hips"),
                Bone("spine.001", "Spine"),
                Bone("spine.002", "Chest"),
                Bone("spine.003", "UpperChest"),
                Bone("spine.004", "Neck"),
                Bone("spine.005", "Head"),
                Bone("thigh.L", "LeftUpperLeg"),
                Bone("shin.L", "LeftLowerLeg"),
                Bone("foot.L", "LeftFoot"),
                Bone("toe.L", "LeftToes"),
                Bone("thigh.R", "RightUpperLeg"),
                Bone("shin.R", "RightLowerLeg"),
                Bone("foot.R", "RightFoot"),
                Bone("toe.R", "RightToes"),
                Bone("shoulder.L", "LeftShoulder"),
                Bone("upper_arm.L", "LeftUpperArm"),
                Bone("forearm.L", "LeftLowerArm"),
                Bone("hand.L", "LeftHand"),
                Bone("shoulder.R", "RightShoulder"),
                Bone("upper_arm.R", "RightUpperArm"),
                Bone("forearm.R", "RightLowerArm"),
                Bone("hand.R", "RightHand")
            };
        }

        static HumanBone Bone(string boneName, string humanName)
        {
            var bone = new HumanBone
            {
                boneName = boneName,
                humanName = humanName
            };
            bone.limit.useDefaultValues = true;
            return bone;
        }

        static SkeletonBone[] BuildSkeleton(GameObject model)
        {
            FieldInfo parentField = typeof(SkeletonBone).GetField(
                "parentName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var bones = new List<SkeletonBone>();
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
            {
                var skeleton = new SkeletonBone
                {
                    name = bone.name,
                    position = bone.localPosition,
                    rotation = bone.localRotation,
                    scale = bone.localScale
                };

                if (parentField != null)
                {
                    object boxed = skeleton;
                    parentField.SetValue(boxed, bone.parent != null ? bone.parent.name : "");
                    skeleton = (SkeletonBone)boxed;
                }

                bones.Add(skeleton);
            }

            return bones.ToArray();
        }

        static void ApplyClipSettings(ModelImporter importer)
        {
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null)
                return;

            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = true;
                clips[i].loopPose = true;
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = false;
            }

            importer.clipAnimations = clips;
        }

        static RuntimeAnimatorController CreateController(AnimationClip idle, AnimationClip walk, AnimationClip run)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Sprint", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idleState = machine.AddState("Idle", new Vector3(300f, 0f, 0f));
            AnimatorState walkState = machine.AddState("Walk", new Vector3(300f, 70f, 0f));
            AnimatorState runState = machine.AddState("Run", new Vector3(300f, 140f, 0f));
            idleState.motion = idle;
            walkState.motion = walk;
            runState.motion = run;
            machine.defaultState = idleState;

            Link(idleState, runState, Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"), Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(idleState, walkState, Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"), Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"));
            Link(walkState, runState, Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(walkState, idleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(runState, walkState, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.2f, "Speed"));
            Link(runState, idleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            return controller;
        }

        static void Link(AnimatorState from, AnimatorState to, params AnimatorCondition[] conditions)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.12f;
            transition.exitTime = 0f;
            transition.offset = 0f;
            for (int i = 0; i < conditions.Length; i++)
                transition.AddCondition(conditions[i].mode, conditions[i].threshold, conditions[i].parameter);
        }

        static AnimatorCondition Condition(AnimatorConditionMode mode, float threshold, string parameter)
        {
            return new AnimatorCondition
            {
                mode = mode,
                threshold = threshold,
                parameter = parameter
            };
        }

        static void AssignPlayer(RuntimeAnimatorController controller)
        {
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPath);
            var walk = player.GetComponent<PlayerWalkAnimation>();
            if (walk != null)
            {
                var so = new SerializedObject(walk);
                so.FindProperty("locomotionController").objectReferenceValue = controller;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var body = player.GetComponent<PlayerController>();
            var bodySo = new SerializedObject(body);
            SerializedProperty characters = bodySo.FindProperty("characterPrefabs");
            GameObject male = AssetDatabase.LoadAssetAtPath<GameObject>(IdlePath);
            if (characters != null && characters.arraySize > 0 && male != null)
                characters.GetArrayElementAtIndex(0).objectReferenceValue = male;
            bodySo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            PrefabUtility.UnloadPrefabContents(player);
        }

        static AnimationClip FindClip(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                    return clip;
            }

            return null;
        }

        static Avatar FindAvatar(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Avatar avatar)
                    return avatar;
            }

            return null;
        }
    }
}
