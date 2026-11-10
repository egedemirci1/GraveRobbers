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
    /// Turns the Rigify characters and the Mixamo walk into Humanoid clips,
    /// then points the player at that shared walk cycle.
    /// </summary>
    public static class CharacterWalkSetup
    {
        const string WalkPath = "Assets/Standard Walk.fbx";
        const string ControllerPath = "Assets/Resources/PlayerWalk.controller";
        const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        const string FlagName = "grave_robbers_add_walk.flag";

        static readonly string[] CharacterModels =
        {
            "Assets/Floreswa/Models/male01_1.fbx",
            "Assets/Floreswa/Models/male02_1.fbx",
            "Assets/Floreswa/Models/male03_1.fbx",
            "Assets/Floreswa/Models/male01_2.fbx"
        };

        static bool _waitingForPlayMode;

        [InitializeOnLoadMethod]
        static void ScheduleRequestedSetup()
        {
            EditorApplication.delayCall += RunIfRequested;
        }

        [MenuItem("Grave Robbers/Setup Character Walk")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Leave Play Mode, then run Grave Robbers/Setup Character Walk.");
                return;
            }

            if (!File.Exists(WalkPath))
            {
                Debug.LogError("Missing walk clip: " + WalkPath);
                return;
            }

            var avatars = new Avatar[CharacterModels.Length];
            for (int i = 0; i < CharacterModels.Length; i++)
            {
                avatars[i] = ConfigureCharacter(CharacterModels[i]);
                if (avatars[i] == null || !avatars[i].isValid || !avatars[i].isHuman)
                {
                    Debug.LogError("Humanoid avatar failed for " + CharacterModels[i]);
                    return;
                }
            }

            AnimationClip clip = ConfigureWalkClip();
            if (clip == null)
            {
                Debug.LogError("Walk clip has no animation.");
                return;
            }

            RuntimeAnimatorController controller = CreateController(clip);
            AssignPlayer(controller, avatars);
            AssetDatabase.SaveAssets();
            Debug.Log("Standard Walk is on the player characters.");
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

        static Avatar ConfigureCharacter(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var description = importer.humanDescription;
            description.human = RigifyMap();
            description.skeleton = BuildSkeleton(model);
            importer.humanDescription = description;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.autoGenerateAvatarMappingIfUnspecified = false;
            importer.SaveAndReimport();
            return FindAvatar(path);
        }

        static AnimationClip ConfigureWalkClip()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(WalkPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.autoGenerateAvatarMappingIfUnspecified = true;

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips != null)
            {
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

            importer.SaveAndReimport();

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(WalkPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                    return clip;
            }

            return null;
        }

        static RuntimeAnimatorController CreateController(AnimationClip clip)
        {
            DirectoryEnsure("Assets/_Project/Animation");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState state = machine.AddState("Walk");
            state.motion = clip;
            machine.defaultState = state;
            return controller;
        }

        static void AssignPlayer(RuntimeAnimatorController controller, Avatar[] avatars)
        {
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            var walk = player.GetComponent<PlayerWalkAnimation>();
            if (walk == null)
                walk = player.AddComponent<PlayerWalkAnimation>();

            var so = new SerializedObject(walk);
            so.FindProperty("controller").objectReferenceValue = controller;
            SerializedProperty avatarProp = so.FindProperty("avatars");
            avatarProp.arraySize = avatars.Length;
            for (int i = 0; i < avatars.Length; i++)
                avatarProp.GetArrayElementAtIndex(i).objectReferenceValue = avatars[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(player);
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

        static Avatar FindAvatar(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Avatar avatar)
                    return avatar;
            }

            return null;
        }

        static void DirectoryEnsure(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                DirectoryEnsure(parent);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
                AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
