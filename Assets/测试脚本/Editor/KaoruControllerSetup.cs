using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using GameAnimatorController = UnityEditor.Animations.AnimatorController;

/// <summary>Builds the Animator graph, then binds it to the scene character.</summary>
public static class KaoruControllerSetup
{
    public const string ControllerPath = "Assets/测试脚本/KaoruGameplay.controller";
    private const string AttackPath = "Assets/测试脚本/KaoruAttack.anim";
    private const string AttackA2Path = "Assets/测试脚本/KaoruAttackA2.anim";
    private const string UpperBodyMaskPath = "Assets/测试脚本/KaoruUpperBody.mask";
    private const string KnifePrefabPath = "Assets/道具/飞刀.prefab";
    private const string AnimationFolder = "Assets/测试女角色/Animations/AnimationsWing/";

    [MenuItem("Tools/Kaoru/Configure Selected Character")]
    public static void ConfigureSelectedCharacter()
    {
        GameObject character = Selection.activeGameObject;
        if (character != null)
        {
            Animator selected = character.GetComponentInParent<Animator>();
            if (selected != null)
                character = selected.gameObject;
        }
        if (character == null || character.GetComponent<Animator>() == null)
            character = GameObject.Find("Kaoru");
        if (character == null || !character.scene.IsValid())
            throw new InvalidOperationException("请先在场景中选中 Kaoru 根对象。");
        Configure(character);
    }

    public static GameAnimatorController BuildAnimator()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 Play 模式再配置 Kaoru。");
        AnimationClip idle = LoadClip("Idle_A");
        AnimationClip run = LoadClip("RunA");
        AnimationClip runLeft = LoadClip("RunA_L");
        AnimationClip runRight = LoadClip("RunA_R");
        AnimationClip walk = LoadClip("Walk");
        AnimationClip walkBack = LoadClip("Walk_back");
        AnimationClip attack = LoadAttackClip(AttackPath, "Atk_A1", "KaoruAttack");
        AnimationClip attackFollow = LoadAttackClip(AttackA2Path, "Atk_A2", "KaoruAttackA2");
        AvatarMask upperBodyMask = LoadUpperBodyMask();

        GameAnimatorController controller = AssetDatabase.LoadAssetAtPath<GameAnimatorController>(ControllerPath);
        if (controller == null)
            controller = GameAnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        // Keep objects the Animator window may be viewing. Replacing the root
        // state machine invalidates its breadcrumb and leaves the graph blank.
        AnimatorStateMachine machine = GetOrCreateMachine(controller, 0, "Base Layer");
        AnimatorStateMachine upperMachine = GetOrCreateMachine(controller, 1, KaoruTopDownController.UpperBodyLayerName);
        AnimatorState idleState = GetOrCreateState(machine, "Idle");
        AnimatorState runState = GetOrCreateState(machine, "RunA");
        AnimatorState walkState = GetOrCreateState(machine, "AttackWalk");
        AnimatorState emptyState = GetOrCreateState(upperMachine, "Empty");
        AnimatorState attackState = GetOrCreateState(upperMachine, "AttackA1");
        AnimatorState followState = GetOrCreateState(upperMachine, "AttackA2");
        BlendTree runBlend = GetOrCreateBlendTree(controller, runState);
        BlendTree walkBlend = GetOrCreateBlendTree(controller, walkState);
        AnimatorState[] baseStates = { idleState, runState, walkState };
        AnimatorState[] upperStates = { emptyState, attackState, followState };
        foreach (AnimatorStateMachine stateMachine in new[] { machine, upperMachine })
        {
            stateMachine.anyStateTransitions = Array.Empty<AnimatorStateTransition>();
            stateMachine.entryTransitions = Array.Empty<AnimatorTransition>();
            stateMachine.stateMachines = Array.Empty<ChildAnimatorStateMachine>();
            stateMachine.entryPosition = new Vector3(20f, 80f);
            stateMachine.anyStatePosition = new Vector3(20f, -80f);
            stateMachine.exitPosition = new Vector3(820f, 260f);
        }
        foreach (AnimatorState state in baseStates.Concat(upperStates))
            state.transitions = Array.Empty<AnimatorStateTransition>();
        machine.states = new[]
        {
            new ChildAnimatorState { state = idleState, position = new Vector3(260f, 80f) },
            new ChildAnimatorState { state = runState, position = new Vector3(570f, 80f) },
            new ChildAnimatorState { state = walkState, position = new Vector3(410f, 300f) }
        };
        upperMachine.states = new[]
        {
            new ChildAnimatorState { state = emptyState, position = new Vector3(260f, 80f) },
            new ChildAnimatorState { state = attackState, position = new Vector3(570f, 80f) },
            new ChildAnimatorState { state = followState, position = new Vector3(570f, 300f) }
        };
        // The old single-layer AttackA1 lived in Base Layer; it is not retained.
        Object[] retained = { controller, machine, upperMachine, idleState, runState, walkState,
            emptyState, attackState, followState, runBlend, walkBlend };
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            if (!retained.Contains(asset))
                Object.DestroyImmediate(asset, true);
        controller.parameters = new[]
        {
            Parameter(KaoruTopDownController.MovingParameterName, AnimatorControllerParameterType.Bool),
            Parameter(KaoruTopDownController.TurnParameterName, AnimatorControllerParameterType.Float),
            Parameter(KaoruTopDownController.AttackParameterName, AnimatorControllerParameterType.Trigger),
            Parameter(KaoruTopDownController.AttackHeldParameterName, AnimatorControllerParameterType.Bool),
            Parameter(KaoruTopDownController.AttackSpeedParameterName, AnimatorControllerParameterType.Float, 1f),
            Parameter(KaoruTopDownController.AimingParameterName, AnimatorControllerParameterType.Bool),
            Parameter(KaoruTopDownController.WalkBackParameterName, AnimatorControllerParameterType.Float)
        };
        controller.layers = new[]
        {
            new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine },
            // Arms, spine and head override the legs, which keep walking below.
            new AnimatorControllerLayer
            {
                name = KaoruTopDownController.UpperBodyLayerName, defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override, avatarMask = upperBodyMask,
                stateMachine = upperMachine
            }
        };
        foreach (AnimatorState state in baseStates.Concat(upperStates))
        {
            state.speed = 1f;
            state.writeDefaultValues = false;
        }

        idleState.motion = idle;
        machine.defaultState = idleState;
        runBlend.name = "RunA Left - Straight - Right";
        runBlend.blendType = BlendTreeType.Simple1D;
        runBlend.blendParameter = KaoruTopDownController.TurnParameterName;
        runBlend.useAutomaticThresholds = false;
        runBlend.children = new[]
        {
            new ChildMotion { motion = runLeft, threshold = -1f, timeScale = 1f },
            new ChildMotion { motion = run, threshold = 0f, timeScale = 1f },
            new ChildMotion { motion = runRight, threshold = 1f, timeScale = 1f }
        };
        runState.motion = runBlend;
        walkBlend.name = "Walk - Walk_back";
        walkBlend.blendType = BlendTreeType.Simple1D;
        walkBlend.blendParameter = KaoruTopDownController.WalkBackParameterName;
        walkBlend.useAutomaticThresholds = false;
        walkBlend.children = new[]
        {
            new ChildMotion { motion = walk, threshold = 0f, timeScale = 1f },
            new ChildMotion { motion = walkBack, threshold = 1f, timeScale = 1f }
        };
        walkState.motion = walkBlend;

        // Legs: Aiming swaps running for Walk / Walk_back; the click has priority.
        AddTransition(idleState, walkState, 0.12f, false,
            (KaoruTopDownController.MovingParameterName, AnimatorConditionMode.If),
            (KaoruTopDownController.AimingParameterName, AnimatorConditionMode.If));
        AddTransition(idleState, runState, 0.12f, false,
            (KaoruTopDownController.MovingParameterName, AnimatorConditionMode.If),
            (KaoruTopDownController.AimingParameterName, AnimatorConditionMode.IfNot));
        AddTransition(runState, walkState, 0.12f, false,
            (KaoruTopDownController.AimingParameterName, AnimatorConditionMode.If));
        AddTransition(runState, idleState, 0.12f, false,
            (KaoruTopDownController.MovingParameterName, AnimatorConditionMode.IfNot));
        AddTransition(walkState, idleState, 0.12f, false,
            (KaoruTopDownController.MovingParameterName, AnimatorConditionMode.IfNot));
        AddTransition(walkState, runState, 0.15f, false,
            (KaoruTopDownController.MovingParameterName, AnimatorConditionMode.If),
            (KaoruTopDownController.AimingParameterName, AnimatorConditionMode.IfNot));
        foreach (AnimatorState state in baseStates)
            foreach (AnimatorStateTransition transition in state.transitions)
                transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;

        // Upper body: an empty state leaves the base pose untouched; one click
        // plays AttackA1 then AttackA2 back to back, uninterruptible. While the
        // button is held, AttackA2 loops straight back into AttackA1.
        emptyState.motion = null;
        upperMachine.defaultState = emptyState;
        foreach (AnimatorState state in new[] { attackState, followState })
        {
            state.tag = KaoruTopDownController.AttackTag;
            state.speedParameter = KaoruTopDownController.AttackSpeedParameterName;
            state.speedParameterActive = true;
        }
        attackState.motion = attack;
        followState.motion = attackFollow;
        AddTransition(emptyState, attackState, 0.06f, false,
            (KaoruTopDownController.AttackParameterName, AnimatorConditionMode.If));
        AddTransition(attackState, followState, 0.08f, true);
        AddTransition(followState, attackState, 0.08f, true,
            (KaoruTopDownController.AttackHeldParameterName, AnimatorConditionMode.If));
        AddTransition(followState, emptyState, 0.15f, true,
            (KaoruTopDownController.AttackHeldParameterName, AnimatorConditionMode.IfNot));

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return controller;
    }

    /// <summary>Returns the knife prefab, adding KaoruThrowingKnife to it if missing.</summary>
    public static KaoruThrowingKnife LoadKnifePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KnifePrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("找不到飞刀 Prefab：" + KnifePrefabPath + "，请在 Knife Thrower 上手动指定。");
            return null;
        }
        if (!prefab.TryGetComponent(out KaoruThrowingKnife knife))
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(KnifePrefabPath);
            try
            {
                contents.AddComponent<KaoruThrowingKnife>();
                PrefabUtility.SaveAsPrefabAsset(contents, KnifePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            knife = AssetDatabase.LoadAssetAtPath<GameObject>(KnifePrefabPath).GetComponent<KaoruThrowingKnife>();
        }
        return knife;
    }

    private static AnimationClip LoadAttackClip(string path, string sourceName, string clipName)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = Object.Instantiate(LoadClip(sourceName));
            AssetDatabase.CreateAsset(clip, path);
        }
        // Preserve an existing independent clip (and any deliberate edits).
        // CopySerialized from an imported FBX also expands its editor curve cache.
        clip.name = clipName;
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static AvatarMask LoadUpperBodyMask()
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
        }
        mask.name = "KaoruUpperBody";
        mask.transformCount = 0;
        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
            mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Body ||
                part == AvatarMaskBodyPart.Head ||
                part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    private static AnimatorStateMachine GetOrCreateMachine(GameAnimatorController controller, int layer, string name)
    {
        AnimatorStateMachine machine = controller.layers.Length > layer ? controller.layers[layer].stateMachine : null;
        if (machine == null)
        {
            machine = new AnimatorStateMachine { hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
        }
        machine.name = name;
        return machine;
    }

    private static BlendTree GetOrCreateBlendTree(GameAnimatorController controller, AnimatorState state)
    {
        BlendTree tree = state.motion as BlendTree;
        if (tree == null)
        {
            tree = new BlendTree { hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller);
        }
        return tree;
    }

    public static void Configure(GameObject character)
    {
        if (character == null || !character.scene.IsValid() || !character.scene.isLoaded)
            throw new ArgumentException("需要已加载场景中的 Kaoru 根对象。", nameof(character));
        GameAnimatorController controller = BuildAnimator();
        Undo.SetCurrentGroupName("Deploy Kaoru Animator");
        Animator animator = character.GetComponent<Animator>();
        if (animator == null)
            animator = Undo.AddComponent<Animator>(character);
        Undo.RecordObject(animator, "Configure Kaoru Animator");
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.speed = 1f;
        Rigidbody body = character.GetComponent<Rigidbody>();
        if (body == null)
            body = Undo.AddComponent<Rigidbody>(character);
        Undo.RecordObject(body, "Configure Kaoru Rigidbody");
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        CapsuleCollider capsule = character.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            capsule = Undo.AddComponent<CapsuleCollider>(character);
            capsule.radius = 0.22f;
            capsule.height = 1.58f;
            capsule.center = new Vector3(0f, 0.79f, 0f);
        }
        KaoruTopDownController player = character.GetComponent<KaoruTopDownController>();
        bool added = player == null;
        if (added)
            player = Undo.AddComponent<KaoruTopDownController>(character);
        Camera camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.gameObject.scene == character.scene && c.isActiveAndEnabled &&
                c.GetComponent<PixelCamera.PixelCameraManager>() != null);
        if (camera == null)
            camera = Camera.main;
        var serializedPlayer = new SerializedObject(player);
        serializedPlayer.FindProperty("gameplayCamera").objectReferenceValue = camera;
        serializedPlayer.FindProperty("animationController").objectReferenceValue = controller;
        // Migrate the old snap turn while preserving deliberate custom tuning.
        SerializedProperty turnSpeed = serializedPlayer.FindProperty("turnSpeed");
        if (added || Mathf.Approximately(turnSpeed.floatValue, 1080f))
            turnSpeed.floatValue = 540f;
        serializedPlayer.ApplyModifiedProperties();
        KaoruKnifeThrower thrower = character.GetComponent<KaoruKnifeThrower>();
        if (thrower == null)
            thrower = Undo.AddComponent<KaoruKnifeThrower>(character);
        if (thrower.knifePrefab == null)
        {
            Undo.RecordObject(thrower, "Assign Kaoru Knife");
            thrower.knifePrefab = LoadKnifePrefab();
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(thrower);
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        ConfigureCameraFollow(character, camera);
        EditorSceneManager.MarkSceneDirty(character.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = character;
        Debug.Log("Kaoru Animator 已部署：Base Layer 为 Idle / RunA / AttackWalk，Upper Body 层由 Trigger 连续播放 AttackA1 → AttackA2，按住左键循环，A1 / A2 各投出一把飞刀；攻击时下半身按移动方向播放 Walk 或 Walk_back。", character);
    }

    [MenuItem("Tools/Kaoru/Configure Camera Follow")]
    public static void ConfigureCameraFollowMenu()
    {
        KaoruTopDownController player = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<KaoruTopDownController>() : null;
        if (player == null)
            player = Object.FindFirstObjectByType<KaoruTopDownController>();
        if (player == null)
            throw new InvalidOperationException("场景中未找到 Kaoru 控制器。");
        ConfigureCameraFollow(player.gameObject);
    }

    public static void ConfigureCameraFollow(GameObject character, Camera camera = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出 Play 模式再配置相机跟随。");
        if (character == null || !character.scene.IsValid() || !character.scene.isLoaded)
            throw new ArgumentException("需要已加载场景中的角色。", nameof(character));
        if (camera == null)
            camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.gameObject.scene == character.scene && c.isActiveAndEnabled &&
                    c.GetComponent<PixelCamera.PixelCameraManager>() != null);
        PixelCamera.PixelCameraManager manager = camera != null
            ? camera.GetComponent<PixelCamera.PixelCameraManager>() : null;
        if (manager == null || manager.transform.parent == null)
            throw new InvalidOperationException("需要具有 PixelCameraSystem 父级的 PixelCamera。");
        Transform rig = manager.transform.parent;
        if (manager.FollowedTransform == null || !manager.FollowedTransform.IsChildOf(rig))
            throw new InvalidOperationException("PixelCamera 的内部跟随锚点必须位于 PixelCameraSystem 内。");

        KaoruPixelCameraFollow follow = rig.GetComponent<KaoruPixelCameraFollow>();
        if (follow == null)
            follow = Undo.AddComponent<KaoruPixelCameraFollow>(rig.gameObject);
        Undo.RecordObject(follow, "Configure Kaoru Camera Follow");
        follow.SetTarget(character.transform);
        follow.enabled = true;
        EditorUtility.SetDirty(follow);
        PrefabUtility.RecordPrefabInstancePropertyModifications(follow);
        KaoruTopDownController player = character.GetComponent<KaoruTopDownController>();
        if (player != null)
        {
            var serializedPlayer = new SerializedObject(player);
            serializedPlayer.FindProperty("gameplayCamera").objectReferenceValue = camera;
            serializedPlayer.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        }
        EditorSceneManager.MarkSceneDirty(character.scene);
        Debug.Log("相机跟随已配置到 " + rig.name + "，目标为 " + character.name + "。", rig);
    }

    private static AnimatorState GetOrCreateState(AnimatorStateMachine machine, string name)
    {
        AnimatorState state = machine.states.FirstOrDefault(child => child.state.name == name).state;
        return state != null ? state : machine.AddState(name);
    }

    private static AnimatorControllerParameter Parameter(string name, AnimatorControllerParameterType type, float value = 0f)
    {
        return new AnimatorControllerParameter { name = name, type = type, defaultFloat = value };
    }

    private static void AddTransition(AnimatorState from, AnimatorState to, float seconds,
        bool exitTime, params (string parameter, AnimatorConditionMode mode)[] conditions)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.name = from.name + " → " + to.name;
        transition.hasExitTime = exitTime;
        transition.exitTime = 1f;
        transition.hasFixedDuration = true;
        transition.duration = seconds;
        transition.offset = 0f;
        transition.canTransitionToSelf = false;
        transition.interruptionSource = TransitionInterruptionSource.None;
        transition.orderedInterruption = true;
        foreach ((string parameter, AnimatorConditionMode mode) in conditions)
            transition.AddCondition(mode, 0f, parameter);
    }

    private static AnimationClip LoadClip(string name)
    {
        string path = AnimationFolder + "Anim@" + name + "_wing.fbx";
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        if (clip == null)
            throw new InvalidOperationException("找不到 Kaoru 动画：" + path);
        return clip;
    }
}
