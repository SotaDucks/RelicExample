using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using GameAnimatorController = UnityEditor.Animations.AnimatorController;

/// <summary>Creates a separate gameplay controller without changing the source assets.</summary>
public static class KaoruControllerSetup
{
    private const string OutputFolder = "Assets/测试脚本";
    private const string ControllerPath = OutputFolder + "/KaoruGameplay.controller";
    private const string AttackPath = OutputFolder + "/KaoruAttack.anim";
    private const string AnimationFolder = "Assets/测试女角色/Animations/AnimationsWing/";

    [MenuItem("Tools/Kaoru/Configure Selected Character")]
    public static void ConfigureSelectedCharacter()
    {
        GameObject character = Selection.activeGameObject;
        if (character == null || character.GetComponent<Animator>() == null)
            character = GameObject.Find("Kaoru");

        if (character == null || !character.scene.IsValid())
        {
            Debug.LogError("请先在场景中选中 Kaoru 根对象，再执行配置菜单。");
            return;
        }

        Configure(character);
    }

    public static void Configure(GameObject character)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("请先退出 Play 模式再配置 Kaoru。");

        AnimationClip idle = LoadClip("Anim@Idle_A_wing.fbx");
        AnimationClip run = LoadClip("Anim@RunA_wing.fbx");
        AnimationClip runLeft = LoadClip("Anim@RunA_L_wing.fbx");
        AnimationClip runRight = LoadClip("Anim@RunA_R_wing.fbx");
        AnimationClip sourceAttack = LoadClip("Anim@Atk_A1_wing.fbx");
        AnimationClip attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackPath);
        if (attack == null)
        {
            attack = Object.Instantiate(sourceAttack);
            AssetDatabase.CreateAsset(attack, AttackPath);
        }
        else
        {
            // Update in place so scene references keep their existing GUID/fileID.
            EditorUtility.CopySerialized(sourceAttack, attack);
        }
        attack.name = "KaoruAttack";
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(attack);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(attack, settings);
        EditorUtility.SetDirty(attack);

        GameAnimatorController controller = AssetDatabase.LoadAssetAtPath<GameAnimatorController>(ControllerPath);
        if (controller == null)
            controller = GameAnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        if (!controller.parameters.Any(parameter => parameter.name == "Turn"))
            controller.AddParameter("Turn", AnimatorControllerParameterType.Float);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idleState = GetOrCreateState(machine, "Idle", new Vector3(250f, 60f));
        AnimatorState runState = GetOrCreateState(machine, "Run", new Vector3(500f, 60f));
        AnimatorState attackState = GetOrCreateState(machine, "Attack", new Vector3(500f, 180f));
        idleState.motion = idle;
        machine.defaultState = idleState;
        attackState.motion = attack;

        BlendTree runBlend = runState.motion as BlendTree;
        if (runBlend == null)
        {
            runBlend = new BlendTree { name = "RunA Turning", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(runBlend, controller);
        }
        runBlend.blendType = BlendTreeType.Simple1D;
        runBlend.blendParameter = "Turn";
        runBlend.useAutomaticThresholds = false;
        runBlend.children = new[]
        {
            new ChildMotion { motion = runLeft, threshold = -1f, timeScale = 1f },
            new ChildMotion { motion = run, threshold = 0f, timeScale = 1f },
            new ChildMotion { motion = runRight, threshold = 1f, timeScale = 1f }
        };
        runState.motion = runBlend;
        // One running state keeps stride phase continuous when turning changes.
        foreach (Object asset in new Object[] { idleState, runState, attackState, machine, runBlend, controller })
            EditorUtility.SetDirty(asset);

        Undo.SetCurrentGroupName("Configure Kaoru Player");
        Animator animator = character.GetComponent<Animator>();
        if (animator == null)
            animator = Undo.AddComponent<Animator>(character);

        Undo.RecordObject(animator, "Configure Kaoru Animator");
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;

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
        if (player == null)
            player = Undo.AddComponent<KaoruTopDownController>(character);

        Camera camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.isActiveAndEnabled && candidate.GetComponent<PixelCamera.PixelCameraManager>() != null);
        if (camera == null)
            camera = Camera.main;

        SerializedObject serializedPlayer = new SerializedObject(player);
        serializedPlayer.FindProperty("gameplayCamera").objectReferenceValue = camera;
        serializedPlayer.FindProperty("animationController").objectReferenceValue = controller;
        serializedPlayer.FindProperty("attackClip").objectReferenceValue = attack;
        serializedPlayer.ApplyModifiedProperties();

        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        EditorSceneManager.MarkSceneDirty(character.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = character;
        Debug.Log("Kaoru 已配置：RunA 奔跑，RunA_L/RunA_R 平滑转向，左键 AttackA1 攻击。", character);
    }

    private static AnimatorState GetOrCreateState(AnimatorStateMachine machine, string name, Vector3 position)
    {
        AnimatorState state = machine.states.FirstOrDefault(child => child.state.name == name).state;
        return state != null ? state : machine.AddState(name, position);
    }

    private static AnimationClip LoadClip(string fileName)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(AnimationFolder + fileName)
            .OfType<AnimationClip>().FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__"));
        if (clip == null)
            throw new System.InvalidOperationException("找不到 Kaoru 动画：" + fileName);
        return clip;
    }
}
