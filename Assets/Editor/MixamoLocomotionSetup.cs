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
        const string CarryIdlePath = "Assets/Carrying-idle.fbx";
        const string CarryWalkPath = "Assets/carrying-Walking.fbx";
        const string CarryRunPath = "Assets/Carrying-Running.fbx";
        const string RifleIdlePath = "Assets/Rifle Idle.fbx";
        const string RifleWalkPath = "Assets/Walk With Rifle.fbx";
        const string RifleRunPath = "Assets/Rifle-Run Forward.fbx";
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

            if (!File.Exists(IdlePath) || !File.Exists(WalkPath) || !File.Exists(RunPath) || !File.Exists(JumpPath)
                || !File.Exists(CarryIdlePath) || !File.Exists(CarryWalkPath) || !File.Exists(CarryRunPath)
                || !File.Exists(RifleIdlePath) || !File.Exists(RifleWalkPath) || !File.Exists(RifleRunPath))
            {
                Debug.LogError("Need the locomotion clips, the carrying clips, and the three rifle clips.");
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
            ConfigureClipToIdlePose(CarryIdlePath, loop: true, bakeHeight: true);
            ConfigureClipToIdlePose(CarryWalkPath, loop: true, bakeHeight: true);
            ConfigureClipToIdlePose(CarryRunPath, loop: true, bakeHeight: true);
            ConfigureClipToIdlePose(RifleIdlePath, loop: true, bakeHeight: true);
            ConfigureClipToIdlePose(RifleWalkPath, loop: true, bakeHeight: true);
            ConfigureClipToIdlePose(RifleRunPath, loop: true, bakeHeight: true);
            AnimationClip walk = FindClip(WalkPath);
            AnimationClip run = FindClip(RunPath);
            AnimationClip jump = FindClip(JumpPath);
            AnimationClip carryIdle = FindClip(CarryIdlePath);
            AnimationClip carryWalk = FindClip(CarryWalkPath);
            AnimationClip carryRun = FindClip(CarryRunPath);
            AnimationClip rifleIdle = FindClip(RifleIdlePath);
            AnimationClip rifleWalk = FindClip(RifleWalkPath);
            AnimationClip rifleRun = FindClip(RifleRunPath);
            if (walk == null || run == null || jump == null || carryIdle == null || carryWalk == null || carryRun == null
                || rifleIdle == null || rifleWalk == null || rifleRun == null)
            {
                Debug.LogError("A locomotion, carrying, or rifle clip has no animation.");
                return;
            }

            RuntimeAnimatorController controller = CreateController(idle, walk, run, jump, carryIdle, carryWalk, carryRun, rifleIdle, rifleWalk, rifleRun);
            AssignPlayer(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("Idle, Walk, Run, Jump, carrying, and rifle clips play on the character's own model.");
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
            AnimationClip jump,
            AnimationClip carryIdle,
            AnimationClip carryWalk,
            AnimationClip carryRun,
            AnimationClip rifleIdle,
            AnimationClip rifleWalk,
            AnimationClip rifleRun)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Sprint", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Carry", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Armed", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idleState = machine.AddState("Idle", new Vector3(300f, 0f, 0f));
            AnimatorState walkState = machine.AddState("Walk", new Vector3(300f, 70f, 0f));
            AnimatorState runState = machine.AddState("Run", new Vector3(300f, 140f, 0f));
            AnimatorState jumpState = machine.AddState("Jump", new Vector3(80f, 70f, 0f));
            AnimatorState carryIdleState = machine.AddState("CarryIdle", new Vector3(560f, 0f, 0f));
            AnimatorState carryWalkState = machine.AddState("CarryWalk", new Vector3(560f, 70f, 0f));
            AnimatorState carryRunState = machine.AddState("CarryRun", new Vector3(560f, 140f, 0f));
            AnimatorState rifleIdleState = machine.AddState("RifleIdle", new Vector3(820f, 0f, 0f));
            AnimatorState rifleWalkState = machine.AddState("RifleWalk", new Vector3(820f, 70f, 0f));
            AnimatorState rifleRunState = machine.AddState("RifleRun", new Vector3(820f, 140f, 0f));
            idleState.motion = idle;
            walkState.motion = walk;
            runState.motion = run;
            jumpState.motion = jump;
            carryIdleState.motion = carryIdle;
            carryWalkState.motion = carryWalk;
            carryRunState.motion = carryRun;
            rifleIdleState.motion = rifleIdle;
            rifleWalkState.motion = rifleWalk;
            rifleRunState.motion = rifleRun;
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

            AnimatorCondition handsFree = Condition(AnimatorConditionMode.IfNot, 0f, "Carry");
            AnimatorCondition notArmed = Condition(AnimatorConditionMode.IfNot, 0f, "Armed");
            AnimatorCondition carrying = Condition(AnimatorConditionMode.If, 0f, "Carry");
            AnimatorCondition armed = Condition(AnimatorConditionMode.If, 0f, "Armed");
            Link(idleState, runState, Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"), Condition(AnimatorConditionMode.If, 0f, "Sprint"), handsFree, notArmed);
            Link(idleState, walkState, Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"), Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), handsFree, notArmed);
            Link(walkState, runState, Condition(AnimatorConditionMode.If, 0f, "Sprint"), handsFree, notArmed);
            Link(walkState, idleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"), handsFree, notArmed);
            Link(runState, walkState, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.2f, "Speed"), handsFree, notArmed);
            Link(runState, idleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"), handsFree, notArmed);

            Link(idleState, rifleRunState, armed, Condition(AnimatorConditionMode.If, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(idleState, rifleWalkState, armed, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(idleState, rifleIdleState, armed, Condition(AnimatorConditionMode.Less, 0.4f, "Speed"));
            Link(walkState, rifleRunState, armed, Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(walkState, rifleWalkState, armed);
            Link(runState, rifleIdleState, armed, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(runState, rifleWalkState, armed, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"));
            Link(runState, rifleRunState, armed);

            Link(idleState, carryRunState, carrying, Condition(AnimatorConditionMode.If, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(idleState, carryWalkState, carrying, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(idleState, carryIdleState, carrying, Condition(AnimatorConditionMode.Less, 0.4f, "Speed"));
            Link(walkState, carryRunState, carrying, Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(walkState, carryWalkState, carrying);
            Link(runState, carryIdleState, carrying, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(runState, carryWalkState, carrying, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"));
            Link(runState, carryRunState, carrying);

            Link(carryIdleState, carryRunState, Condition(AnimatorConditionMode.If, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(carryIdleState, carryWalkState, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(carryWalkState, carryRunState, Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(carryWalkState, carryIdleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(carryRunState, carryWalkState, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.2f, "Speed"));
            Link(carryRunState, carryIdleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));

            Link(carryRunState, runState, handsFree, notArmed, Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(carryWalkState, walkState, handsFree, notArmed);
            Link(carryIdleState, idleState, handsFree, notArmed);
            Link(carryRunState, idleState, handsFree, notArmed, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(carryWalkState, idleState, handsFree, notArmed, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));

            Link(rifleIdleState, rifleRunState, Condition(AnimatorConditionMode.If, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(rifleIdleState, rifleWalkState, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(rifleWalkState, rifleRunState, Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(rifleWalkState, rifleIdleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(rifleRunState, rifleWalkState, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.2f, "Speed"));
            Link(rifleRunState, rifleIdleState, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(rifleRunState, runState, notArmed, handsFree, Condition(AnimatorConditionMode.If, 0f, "Sprint"));
            Link(rifleWalkState, walkState, notArmed, handsFree);
            Link(rifleIdleState, idleState, notArmed, handsFree);
            Link(rifleRunState, idleState, notArmed, handsFree, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));
            Link(rifleWalkState, idleState, notArmed, handsFree, Condition(AnimatorConditionMode.Less, 0.2f, "Speed"));

            AnimatorStateTransition intoJump = machine.AddAnyStateTransition(jumpState);
            intoJump.hasExitTime = false;
            intoJump.hasFixedDuration = true;
            intoJump.duration = 0.08f;
            intoJump.offset = 0.34f;
            intoJump.canTransitionToSelf = false;
            intoJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            Link(jumpState, rifleRunState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), armed, Condition(AnimatorConditionMode.If, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(jumpState, rifleWalkState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), armed, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(jumpState, rifleIdleState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), armed, Condition(AnimatorConditionMode.Less, 0.4f, "Speed"));
            Link(jumpState, carryRunState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), carrying, Condition(AnimatorConditionMode.If, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(jumpState, carryWalkState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), carrying, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(jumpState, carryIdleState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), carrying, Condition(AnimatorConditionMode.Less, 0.4f, "Speed"));
            Link(jumpState, runState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), handsFree, notArmed, Condition(AnimatorConditionMode.If, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(jumpState, walkState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), handsFree, notArmed, Condition(AnimatorConditionMode.IfNot, 0f, "Sprint"), Condition(AnimatorConditionMode.Greater, 0.4f, "Speed"));
            Link(jumpState, idleState, Condition(AnimatorConditionMode.If, 0f, "Grounded"), handsFree, notArmed, Condition(AnimatorConditionMode.Less, 0.4f, "Speed"));
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
