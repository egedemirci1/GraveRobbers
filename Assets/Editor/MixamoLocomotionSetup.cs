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
        const string RunPath = "Assets/Fast Run (1).fbx";
        const string JumpPath = "Assets/Jumping.fbx";
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

            if (!File.Exists(IdlePath) || !File.Exists(WalkPath) || !File.Exists(RunPath) || !File.Exists(JumpPath))
            {
                Debug.LogError("Need Assets/Idle.fbx, Assets/Walking.fbx, Assets/Fast Run (1).fbx, and Assets/Jumping.fbx.");
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

            // Motion clips were given avatars from their own first frame, so the
            // muscle axes do not match Idle. Rebuild those avatars on Idle's pose.
            ConfigureClipToIdlePose(WalkPath, loop: true, bakeHeight: true);
            ConfigureClipToIdlePose(RunPath, loop: true, bakeHeight: true);
            // Jump's vertical travel stays root motion. The capsule already moves the
            // body, so baking that height into the pose lifts the mesh and drops it
            // again around the middle of the clip.
            ConfigureClipToIdlePose(JumpPath, loop: false, bakeHeight: false);
            AnimationClip walk = FindClip(WalkPath);
            AnimationClip run = FindClip(RunPath);
            AnimationClip jump = FindClip(JumpPath);
            if (walk == null || run == null || jump == null)
            {
                Debug.LogError("Walking.fbx, Fast Run (1).fbx, or Jumping.fbx has no animation clip.");
                return;
            }

            RuntimeAnimatorController controller = CreateController(idle, walk, run, jump);
            AssignPlayer(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("Idle, Walk, Run, and Jump play on the character's own model.");
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

        static void ConfigureClipToIdlePose(string path, bool loop, bool bakeHeight)
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
            ApplyClipSettings(importer, loop, bakeHeight);
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

        static void ApplyClipSettings(ModelImporter importer, bool loop = true, bool bakeHeight = true)
        {
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null)
                return;

            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = bakeHeight;
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = false;
            }

            importer.clipAnimations = clips;
        }

        static RuntimeAnimatorController CreateController(
            AnimationClip idle,
            AnimationClip walk,
            AnimationClip run,
            AnimationClip jump)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Sprint", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idleState = machine.AddState("Idle", new Vector3(300f, 0f, 0f));
            AnimatorState walkState = machine.AddState("Walk", new Vector3(300f, 70f, 0f));
            AnimatorState runState = machine.AddState("Run", new Vector3(300f, 140f, 0f));
            AnimatorState jumpState = machine.AddState("Jump", new Vector3(520f, 70f, 0f));
            idleState.motion = idle;
            walkState.motion = walk;
            runState.motion = run;
            jumpState.motion = jump;
            // The capsule already arcs through the air. Sped-up playback of the
            // whole clip (crouch, flight, and landing) fights that arc.
            jumpState.speed = 1f;
            machine.defaultState = idleState;

            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name != "Grounded")
                    continue;

                parameters[i].defaultBool = true;
                controller.parameters = parameters;
                break;
            }

            Link(idleState, runState, Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"), Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(idleState, walkState, Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"), Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"));
            Link(walkState, runState, Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(walkState, idleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(runState, walkState, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.2f, "Speed"));
            Link(runState, idleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));

            AnimatorStateTransition intoJump = machine.AddAnyStateTransition(jumpState);
            intoJump.hasExitTime = false;
            intoJump.hasFixedDuration = true;
            intoJump.duration = 0.08f;
            intoJump.offset = 0.34f;
            intoJump.canTransitionToSelf = false;
            intoJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            Link(jumpState, runState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), Condition(AnimatorConditionMode.If, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(jumpState, walkState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(jumpState, idleState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), Condition(AnimatorConditionMode.Less, 0.4f, "Speed"));
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
            GameObject[] bodies =
            {
                AssetDatabase.LoadAssetAtPath<GameObject>(IdlePath),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Floreswa/Prefabs/male02_1.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Floreswa/Prefabs/male03_1.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Floreswa/Prefabs/male01_2.prefab")
            };
            if (characters != null)
            {
                characters.arraySize = bodies.Length;
                for (int i = 0; i < bodies.Length; i++)
                    characters.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            }
            bodySo.ApplyModifiedPropertiesWithoutUndo();

            if (walk != null)
            {
                var pose = new SerializedObject(walk);
                pose.FindProperty("poseSource").objectReferenceValue = bodies[0];
                pose.ApplyModifiedPropertiesWithoutUndo();
            }

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
