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
        public string expected;
        public Action action;
        public float minTurn = 10f, maxTurn = -10f, maxSpeed, finalSpeed, phase;
        public float leftWeight, rightWeight, straightWeight, maxAttackPhase;
        public bool sawAttack, sawRun, sawIdle;
        public int frames;
    }
    static KaoruTopDownController player;
    static Animator animator;
    static Rigidbody body;
    static Keyboard keyboard, previousKeyboard;
    static Mouse mouse, previousMouse;
    static InputSettings.BackgroundBehavior background;
    static readonly List<Stage> stages = new List<Stage>();
    static readonly List<string> failures = new List<string>();
    static readonly List<float> attackDurations = new List<float>();
    static int index, lastFrame = -1;
    static float started, attackStart;
    static bool wasAttacking, screenCaptured, running;
    static int attackEntries;
    static Vector3 initialPosition;
    static Quaternion initialRotation;
    static float maxLockedSpeed;
    static FieldInfo speedField = typeof(KaoruTopDownController).GetField("attackSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
    static FieldInfo cameraField = typeof(KaoruTopDownController).GetField("gameplayCamera", BindingFlags.NonPublic | BindingFlags.Instance);
    static float originalAttackSpeed;

    [MenuItem("Tools/Kaoru/Verify In Play Mode")]
    public static void VerifyMenu() { Debug.Log(Start()); }

    public static string Start()
    {
        if (running) throw new Exception("Verification is already running");
        stages.Clear(); failures.Clear(); attackDurations.Clear(); index=0; lastFrame=-1; wasAttacking=false; attackEntries=0; maxLockedSpeed=0f;
        player = UnityEngine.Object.FindFirstObjectByType<KaoruTopDownController>();
        if (!Application.isPlaying || player == null) throw new Exception("Enter Play mode with Kaoru before running verification.");
        System.IO.Directory.CreateDirectory("Temp/KaoruAnimatorVerification");
        body = player.GetComponent<Rigidbody>(); animator = player.GetComponent<Animator>();
        initialPosition = body.position; initialRotation = body.rotation;
        originalAttackSpeed = (float)speedField.GetValue(player);
        previousKeyboard = Keyboard.current; previousMouse = Mouse.current;
        background = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
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
        Add("IdleAttackPlaying", .35f, "AttackA1");
        Add("RepeatedClickIgnored", .05f, "AttackA1", null, true);
        Add("IdleAttackRecovery", 1.4f, "Idle");
        Add("RunAgain", .35f, "RunA", new[]{Key.W});
        Add("RunAttackClick", .05f, null, new[]{Key.W}, true);
        Add("RunAttackPlaying", .35f, "AttackA1", new[]{Key.W});
        Add("ReleaseDuringAttack", .3f, "AttackA1");
        Add("ReleaseReturnsIdle", 1.0f, "Idle");
        Add("RunBeforeHeldAttack", .35f, "RunA", new[]{Key.W});
        Add("HeldAttackClick", .05f, null, new[]{Key.W}, true);
        Add("HeldAttackPlaying", .35f, "AttackA1", new[]{Key.W});
        Add("HeldAttackReturnsRun", 1.4f, "RunA", new[]{Key.W});
        Add("DisableAttackClick", .05f, null, null, true);
        Add("DisableAttackPlaying", .25f, "AttackA1");
        Add("Disabled", .1f, null, null, false, () => player.enabled=false);
        Add("ReenabledIdle", .25f, "Idle", null, false, () => player.enabled=true);
        Add("RunAfterEnable", .35f, "RunA", new[]{Key.W});
        Add("StopBeforeFastAttack", .3f, "Idle");
        Add("FastAttackClick", .05f, null, null, true, () => speedField.SetValue(player,2f));
        Add("FastAttackPlaying", .2f, "AttackA1");
        Add("FastAttackReturnsIdle", .8f, "Idle");
        EnterStage();
        running=true;
        EditorApplication.update += Tick;
        return "Started 29-stage Play mode input verification";
    }
    static void Add(string name, float seconds, string expected, Key[] keys=null, bool down=false, Action action=null)
    { stages.Add(new Stage{name=name,seconds=seconds,expected=expected,keys=keys??new Key[0],mouseDown=down,action=action}); }
    static void EnterStage()
    {
        var stage=stages[index]; stage.action?.Invoke(); started=Time.time; screenCaptured=false;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(stage.keys));
        var state=new MouseState{position=new Vector2(Screen.width*.7f,Screen.height*.55f)};
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
            if(info.IsName("AttackA1")) {s.sawAttack=true;s.maxAttackPhase=Mathf.Max(s.maxAttackPhase,info.normalizedTime);}
            bool attacking=player.IsAttacking;
            if(attacking && !wasAttacking) {attackEntries++;attackStart=Time.time;}
            if(!attacking && wasAttacking) attackDurations.Add(Time.time-attackStart);
            wasAttacking=attacking;
            if(attacking && player.enabled) maxLockedSpeed=Mathf.Max(maxLockedSpeed,velocity);
            if(!screenCaptured && Time.time-started>.10f && (s.name=="RightTurn" || s.name=="LeftTurn" || s.name=="IdleAttackPlaying"))
            {ScreenCapture.CaptureScreenshot("Temp/KaoruAnimatorVerification/"+s.name+".png");screenCaptured=true;}
            if(Time.time-started<s.seconds) return;
            if(s.expected!=null && !info.IsName(s.expected)) failures.Add(s.name+": expected "+s.expected+", got "+info.shortNameHash);
            if(s.name=="RightTurn" && (s.maxTurn<.1f || s.rightWeight<.1f)) failures.Add("Right turn did not use RunA_R");
            if(s.name=="LeftTurn" && (s.minTurn>-.1f || s.leftWeight<.1f)) failures.Add("Left turn did not use RunA_L");
            if(s.name=="Diagonal" && Mathf.Abs(s.finalSpeed-4f)>.15f) failures.Add("Diagonal running speed differs from 4");
            if(s.name=="Stop" && s.finalSpeed>.05f) failures.Add("Movement did not stop");
            if(s.name=="RunForward" && s.straightWeight<.95f) failures.Add("Straight running did not settle on RunA");
            if(s.name=="ReenabledIdle" && player.IsAttacking) failures.Add("Reenabled player is locked in attack");
            index++;
            if(index>=stages.Count) Finish(); else EnterStage();
        }
        catch(Exception e) {failures.Add(e.ToString());Finish();}
    }
    static void Finish()
    {
        EditorApplication.update-=Tick; running=false;
        if(maxLockedSpeed>.05f) failures.Add("Attack horizontal speed: "+maxLockedSpeed);
        if(attackEntries!=5) failures.Add("Expected 5 accepted attacks, got "+attackEntries);
        if(attackDurations.Count>=5)
        {
            if(attackDurations[0]<1.1f || attackDurations[0]>1.6f) failures.Add("Normal attack duration mismatch: "+attackDurations[0]);
            if(attackDurations[4]<.55f || attackDurations[4]>.9f) failures.Add("2x attack duration mismatch: "+attackDurations[4]);
        }
        var runningStages=stages.Where(s=>new[]{"RunForward","RightTurn","LeftTurn","Reverse","Diagonal"}.Contains(s.name)).ToArray();
        for(int i=1;i<runningStages.Length;i++) if(runningStages[i].phase<runningStages[i-1].phase) failures.Add("Run phase reset at "+runningStages[i].name);
        if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if(mouse!=null && mouse.added) InputSystem.RemoveDevice(mouse);
        if(previousKeyboard!=null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        if(previousMouse!=null && previousMouse.added) previousMouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior=background;
        if (player != null && body != null)
        {
            speedField.SetValue(player,originalAttackSpeed);
            player.enabled=false;
            body.position=initialPosition;body.rotation=initialRotation;body.linearVelocity=Vector3.zero;
            player.enabled=true;
        }
        var report=new {passed=failures.Count==0,completedStages=index,attackEntries,attackDurations,maxLockedSpeed,failures,stages};
        System.IO.File.WriteAllText("Temp/KaoruAnimatorVerification/playmode-report.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        Debug.Log("Kaoru Play verification: "+(failures.Count==0?"PASS":"FAIL")+"; "+string.Join("; ",failures));
    }
}

