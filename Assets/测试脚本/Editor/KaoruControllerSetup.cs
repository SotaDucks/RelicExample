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
        AnimationClip sourceAttack = LoadClip("Atk_A1");
        AnimationClip attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackPath);
        if (attack == null)
        {
            attack = Object.Instantiate(sourceAttack);
            AssetDatabase.CreateAsset(attack, AttackPath);
        }
        // Preserve an existing independent clip (and any deliberate edits).
        // CopySerialized from an imported FBX also expands its editor curve cache.
        attack.name = "KaoruAttack";
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(attack);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(attack, settings);
        EditorUtility.SetDirty(attack);

        GameAnimatorController controller = AssetDatabase.LoadAssetAtPath<GameAnimatorController>(ControllerPath);
        if (controller == null)
            controller = GameAnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        // Keep objects the Animator window may be viewing. Replacing the root
        // state machine invalidates its breadcrumb and leaves the graph blank.
        AnimatorStateMachine machine = controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;
        if (machine == null)
        {
            machine = new AnimatorStateMachine { name = "Base Layer", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
        }
        AnimatorState idleState = GetOrCreateState(machine, "Idle");
        AnimatorState runState = GetOrCreateState(machine, "RunA");
        AnimatorState attackState = GetOrCreateState(machine, "AttackA1");
        BlendTree runBlend = runState.motion as BlendTree;
        if (runBlend == null)
        {
            runBlend = new BlendTree { hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(runBlend, controller);
        }
        foreach (AnimatorState state in new[] { idleState, runState, attackState })
            state.transitions = Array.Empty<AnimatorStateTransition>();
        machine.anyStateTransitions = Array.Empty<AnimatorStateTransition>();
        machine.entryTransitions = Array.Empty<AnimatorTransition>();
        machine.stateMachines = Array.Empty<ChildAnimatorStateMachine>();
        machine.states = new[]
        {
            new ChildAnimatorState { state = idleState, position = new Vector3(260f, 80f) },
            new ChildAnimatorState { state = runState, position = new Vector3(570f, 80f) },
            new ChildAnimatorState { state = attackState, position = new Vector3(410f, 300f) }
        };
        Object[] retained = { controller, machine, idleState, runState, attackState, runBlend };
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            if (!retained.Contains(asset))
                Object.DestroyImmediate(asset, true);
        controller.parameters = new[]
        {
            Parameter(KaoruTopDownController.MovingParameterName, AnimatorControllerParameterType.Bool),
            Parameter(KaoruTopDownController.TurnParameterName, AnimatorControllerParameterType.Float),
            Parameter(KaoruTopDownController.AttackParameterName, AnimatorControllerParameterType.Trigger),
            Parameter(KaoruTopDownController.AttackSpeedParameterName, AnimatorControllerParameterType.Float, 1f)
        };
        controller.layers = new[] { new AnimatorControllerLayer { name = "Base Layer", defaultWeight = 1f, stateMachine = machine } };
        machine.entryPosition = new Vector3(20f, 80f);
        machine.anyStatePosition = new Vector3(20f, -80f);
        machine.exitPosition = new Vector3(820f, 260f);
        foreach (AnimatorState state in new[] { idleState, runState, attackState })
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
        attackState.motion = attack;
        attackState.tag = KaoruTopDownController.AttackTag;
        attackState.speedParameter = KaoruTopDownController.AttackSpeedParameterName;
        attackState.speedParameterActive = true;
        // Attack edges come first: a click has priority over movement changes.
        AddTransition(idleState, attackState, 0.06f, false,
            KaoruTopDownController.AttackParameterName, AnimatorConditionMode.If);
        AddTransition(runState, attackState, 0.06f, false,
            KaoruTopDownController.AttackParameterName, AnimatorConditionMode.If);
        AddTransition(idleState, runState, 0.12f, false,
            KaoruTopDownController.MovingParameterName, AnimatorConditionMode.If);
        AddTransition(runState, idleState, 0.12f, false,
            KaoruTopDownController.MovingParameterName, AnimatorConditionMode.IfNot);
        AddTransition(attackState, runState, 0.1f, true,
            KaoruTopDownController.MovingParameterName, AnimatorConditionMode.If);
        AddTransition(attackState, idleState, 0.1f, true,
            KaoruTopDownController.MovingParameterName, AnimatorConditionMode.IfNot);
        // A click may interrupt the Idle/RunA blend, but never an attack.
        foreach (AnimatorState state in new[] { idleState, runState })
            foreach (AnimatorStateTransition transition in state.transitions)
                if (transition.destinationState != attackState)
                    transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return controller;
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
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        ConfigureCameraFollow(character, camera);
        EditorSceneManager.MarkSceneDirty(character.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = character;
        Debug.Log("Kaoru Animator 已部署：Idle ↔ RunA，Trigger 进入 AttackA1，完整播放后按 IsMoving 返回；Turn 混合 RunA_L / RunA / RunA_R。", character);
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
        bool exitTime, string parameter, AnimatorConditionMode condition)
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
        transition.AddCondition(condition, 0f, parameter);
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
