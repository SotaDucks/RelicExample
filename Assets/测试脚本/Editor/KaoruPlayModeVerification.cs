using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEditor;

public static class KaoruPlayModeVerification
{
    // Editor-only integration checks driven by virtual Input System devices.
    // The report and captured frames remain outside Assets, under Temp/.
    public sealed class Stage
    {
        public string name;
        public float seconds;
        public Key[] keys = new Key[0];
        public bool mouseDown;
        public string expected, expectedUpper;
        public Action action;
        public float minTurn = 10f, maxTurn = -10f, maxSpeed, finalSpeed, phase;
        public float leftWeight, rightWeight, straightWeight, walkBack, aimError;
        public bool sawAttack, sawRun, sawIdle;
        public int frames;
    }
    static KaoruTopDownController player;
    static Animator animator;
    static Rigidbody body;
    static Keyboard keyboard, previousKeyboard;
    static Mouse mouse, previousMouse;
    static InputSettings.BackgroundBehavior background;
    static InputSettings.EditorInputBehaviorInPlayMode editorInput;
    static readonly List<Stage> stages = new List<Stage>();
    static readonly List<string> failures = new List<string>();
    static readonly List<float> attackDurations = new List<float>();
    static int index, lastFrame = -1;
    static float started, attackStart;
    static bool wasAttacking, screenCaptured, running;
    static int attackEntries;
    static Vector3 initialPosition;
    static Quaternion initialRotation;
    static FieldInfo speedField = typeof(KaoruTopDownController).GetField("attackSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
    static FieldInfo cameraField = typeof(KaoruTopDownController).GetField("gameplayCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    static FieldInfo runSpeedField = typeof(KaoruTopDownController).GetField("runSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
    static FieldInfo attackMoveSpeedField = typeof(KaoruTopDownController).GetField("attackMoveSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly List<InputDevice> disabledDevices = new List<InputDevice>();
    static float runSpeed, attackMoveSpeed;
    static float originalAttackSpeed;
    static readonly Vector2 MouseViewport = new Vector2(.7f, .55f);

    [MenuItem("Tools/Kaoru/Verify In Play Mode")]
    public static void VerifyMenu() { Debug.Log(Start()); }

    public static string Start()
    {
        if (running) throw new Exception("Verification is already running");
        stages.Clear(); failures.Clear(); attackDurations.Clear(); index=0; lastFrame=-1; wasAttacking=false; attackEntries=0;
        player = UnityEngine.Object.FindFirstObjectByType<KaoruTopDownController>();
        if (!Application.isPlaying || player == null) throw new Exception("Enter Play mode with Kaoru before running verification.");
        System.IO.Directory.CreateDirectory("Temp/KaoruAnimatorVerification");
        body = player.GetComponent<Rigidbody>(); animator = player.GetComponent<Animator>();
        initialPosition = body.position; initialRotation = body.rotation;
        originalAttackSpeed = (float)speedField.GetValue(player);
        // Durations below assume 1x, then FastAttackClick switches to 2x.
        speedField.SetValue(player, 1f);
        runSpeed = (float)runSpeedField.GetValue(player);
        attackMoveSpeed = (float)attackMoveSpeedField.GetValue(player);
        previousKeyboard = Keyboard.current; previousMouse = Mouse.current;
        background = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        // Virtual devices must reach the game even when the Game view is unfocused.
        editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        // Real devices would otherwise reclaim Keyboard/Mouse.current mid-run.
        disabledDevices.Clear();
        foreach (var device in InputSystem.devices.ToArray())
            if ((device is Keyboard || device is UnityEngine.InputSystem.Pointer) && device.enabled)
            { InputSystem.DisableDevice(device); disabledDevices.Add(device); }
        keyboard = InputSystem.AddDevice<Keyboard>("KaoruValidationKeyboard");
        mouse = InputSystem.AddDevice<Mouse>("KaoruValidationMouse");
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        var camera = (Camera)cameraField.GetValue(player);
        body.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up));
        player.enabled = false; player.enabled = true;
        Add("Idle", .25f, "Idle");
        Add("RunForward", .5f, "RunA", new[]{Key.W});
        Add("RightTurn", .25f, "RunA", new[]{Key.W,Key.D});
        Add("LeftTurn", .25f, "RunA", new[]{Key.W});
        Add("Reverse", .45f, "RunA", new[]{Key.S});
        Add("Diagonal", .35f, "RunA", new[]{Key.W,Key.D});
        Add("Stop", .3f, "Idle");
        Add("IdleAttackClick", .05f, null, null, true);
        Add("IdleAttackA1", .35f, "Idle", null, false, null, "AttackA1");
        Add("RepeatedClickIgnored", .05f, "Idle", null, true, null, "AttackA1");
        Add("IdleComboA2", .95f, "Idle", null, false, null, "AttackA2");
        Add("IdleComboEnds", 1.1f, "Idle", null, false, null, "Empty");
        Add("RunAgain", .35f, "RunA", new[]{Key.W});
        Add("RunAttackClick", .05f, null, new[]{Key.W}, true);
        Add("ForwardAttackWalk", .5f, "AttackWalk", new[]{Key.D}, false, null, "AttackA1");
        Add("BackwardAttackWalk", .6f, "AttackWalk", new[]{Key.A});
        Add("AttackReturnsRun", 1.4f, "RunA", new[]{Key.W}, false, null, "Empty");
        Add("DisableAttackClick", .05f, null, null, true);
        Add("DisableAttackPlaying", .25f, "Idle", null, false, null, "AttackA1");
        Add("Disabled", .1f, null, null, false, () => player.enabled=false);
        Add("ReenabledIdle", .25f, "Idle", null, false, () => player.enabled=true, "Empty");
        Add("RunAfterEnable", .35f, "RunA", new[]{Key.W});
        Add("StopBeforeFastAttack", .3f, "Idle");
        Add("FastAttackClick", .05f, null, null, true, () => speedField.SetValue(player,2f));
        Add("FastAttackPlaying", .2f, "Idle", null, false, null, "AttackA1");
        Add("FastAttackReturnsIdle", 1.2f, "Idle", null, false, null, "Empty");
        EnterStage();
        running=true;
        EditorApplication.update += Tick;
        return "Started "+stages.Count+"-stage Play mode input verification";
    }
    static void Add(string name, float seconds, string expected, Key[] keys=null, bool down=false, Action action=null, string upper=null)
    { stages.Add(new Stage{name=name,seconds=seconds,expected=expected,expectedUpper=upper,keys=keys??new Key[0],mouseDown=down,action=action}); }
    static void EnterStage()
    {
        var stage=stages[index]; stage.action?.Invoke(); started=Time.time; screenCaptured=false;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(stage.keys));
        var state=new MouseState{position=new Vector2(Screen.width*MouseViewport.x,Screen.height*MouseViewport.y)};
        if(stage.mouseDown) state=state.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(mouse,state);
    }
    static void Tick()
    {
        try
        {
            if (!Application.isPlaying) { failures.Add("Play mode stopped unexpectedly"); Finish(); return; }
            if(Time.frameCount==lastFrame) return;
            lastFrame=Time.frameCount;
            var s=stages[index]; s.frames++;
            var info=animator.GetCurrentAnimatorStateInfo(0);
            var turn=animator.GetFloat("Turn");
            s.minTurn=Mathf.Min(s.minTurn,turn); s.maxTurn=Mathf.Max(s.maxTurn,turn);
            var velocity=Vector3.ProjectOnPlane(body.linearVelocity,Vector3.up).magnitude;
            s.maxSpeed=Mathf.Max(s.maxSpeed,velocity); s.finalSpeed=velocity;
            if(info.IsName("RunA"))
            {
                s.sawRun=true; s.phase=info.normalizedTime;
                foreach(var clip in animator.GetCurrentAnimatorClipInfo(0))
                {
                    if(clip.clip.name=="RunA_L_wing") s.leftWeight=Mathf.Max(s.leftWeight,clip.weight);
                    if(clip.clip.name=="RunA_R_wing") s.rightWeight=Mathf.Max(s.rightWeight,clip.weight);
                    if(clip.clip.name=="RunA_wing") s.straightWeight=Mathf.Max(s.straightWeight,clip.weight);
                }
            }
            s.sawIdle |= info.IsName("Idle");
            var upper=animator.GetCurrentAnimatorStateInfo(1);
            s.sawAttack |= upper.IsTag(KaoruTopDownController.AttackTag);
            s.walkBack=animator.GetFloat(KaoruTopDownController.WalkBackParameterName);
            s.aimError=AimError();
            bool attacking=player.IsAttacking;
            if(attacking && !wasAttacking) {attackEntries++;attackStart=Time.time;}
            if(!attacking && wasAttacking) attackDurations.Add(Time.time-attackStart);
            wasAttacking=attacking;
            if(!screenCaptured && Time.time-started>.10f && (s.name=="RightTurn" || s.name=="IdleAttackA1" || s.name=="ForwardAttackWalk" || s.name=="BackwardAttackWalk"))
            {ScreenCapture.CaptureScreenshot("Temp/KaoruAnimatorVerification/"+s.name+".png");screenCaptured=true;}
            if(Time.time-started<s.seconds) return;
            if(s.expected!=null && !info.IsName(s.expected)) failures.Add(s.name+": expected "+s.expected+", got "+info.shortNameHash);
            if(s.expectedUpper!=null && !upper.IsName(s.expectedUpper)) failures.Add(s.name+": expected upper "+s.expectedUpper+", got "+upper.shortNameHash);
            if((s.name=="ForwardAttackWalk" || s.name=="BackwardAttackWalk") && Mathf.Abs(s.finalSpeed-attackMoveSpeed)>.15f) failures.Add(s.name+": attack walk speed "+s.finalSpeed);
            if(s.name=="ForwardAttackWalk" && s.walkBack>.1f) failures.Add("Forward attack walk used Walk_back: "+s.walkBack);
            if(s.name=="BackwardAttackWalk" && s.walkBack<.9f) failures.Add("Backward attack walk did not use Walk_back: "+s.walkBack);
            if((s.name=="IdleAttackA1" || s.name=="ForwardAttackWalk" || s.name=="BackwardAttackWalk") && s.aimError>8f) failures.Add(s.name+": body is "+s.aimError+" degrees off the mouse aim");
            if(s.name=="RightTurn" && (s.maxTurn<.1f || s.rightWeight<.1f)) failures.Add("Right turn did not use RunA_R");
            if(s.name=="LeftTurn" && (s.minTurn>-.1f || s.leftWeight<.1f)) failures.Add("Left turn did not use RunA_L");
            if(s.name=="Diagonal" && Mathf.Abs(s.finalSpeed-runSpeed)>.15f) failures.Add("Diagonal running speed differs from "+runSpeed+": "+s.finalSpeed);
            if(s.name=="Stop" && s.finalSpeed>.05f) failures.Add("Movement did not stop");
            if(s.name=="RunForward" && s.straightWeight<.95f) failures.Add("Straight running did not settle on RunA");
            if(s.name=="ReenabledIdle" && player.IsAttacking) failures.Add("Reenabled player is locked in attack");
            index++;
            if(index>=stages.Count) Finish(); else EnterStage();
        }
        catch(Exception e) {failures.Add(e.ToString());Finish();}
    }
    // Angle between the body and the ground point under the virtual mouse.
    static float AimError()
    {
        var camera=(Camera)cameraField.GetValue(player);
        var ray=camera.ViewportPointToRay(MouseViewport);
        if(!new Plane(Vector3.up,body.position).Raycast(ray,out float distance)) return 180f;
        var aim=Vector3.ProjectOnPlane(ray.GetPoint(distance)-body.position,Vector3.up);
        return Vector3.Angle(body.rotation*Vector3.forward,aim);
    }
    static void Finish()
    {
        EditorApplication.update-=Tick; running=false;
        if(attackEntries!=4) failures.Add("Expected 4 accepted attacks, got "+attackEntries);
        // One click = AttackA1 (1.17 s) + AttackA2 (0.75 s) + blends.
        if(attackDurations.Count>=4)
        {
            if(attackDurations[0]<1.9f || attackDurations[0]>2.4f) failures.Add("Normal combo duration mismatch: "+attackDurations[0]);
            if(attackDurations[3]<.95f || attackDurations[3]>1.3f) failures.Add("2x combo duration mismatch: "+attackDurations[3]);
        }
        var runningStages=stages.Where(s=>new[]{"RunForward","RightTurn","LeftTurn","Reverse","Diagonal"}.Contains(s.name)).ToArray();
        for(int i=1;i<runningStages.Length;i++) if(runningStages[i].phase<runningStages[i-1].phase) failures.Add("Run phase reset at "+runningStages[i].name);
        if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if(mouse!=null && mouse.added) InputSystem.RemoveDevice(mouse);
        foreach(var device in disabledDevices) if(device.added) InputSystem.EnableDevice(device);
        disabledDevices.Clear();
        if(previousKeyboard!=null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        if(previousMouse!=null && previousMouse.added) previousMouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior=background;
        InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
        if (player != null && body != null)
        {
            speedField.SetValue(player,originalAttackSpeed);
            player.enabled=false;
            body.position=initialPosition;body.rotation=initialRotation;body.linearVelocity=Vector3.zero;
            player.enabled=true;
        }
        var report=new {passed=failures.Count==0,completedStages=index,attackEntries,attackDurations,failures,stages};
        System.IO.File.WriteAllText("Temp/KaoruAnimatorVerification/playmode-report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        Debug.Log("Kaoru Play verification: "+(failures.Count==0?"PASS":"FAIL")+"; "+string.Join("; ",failures));
    }
}

