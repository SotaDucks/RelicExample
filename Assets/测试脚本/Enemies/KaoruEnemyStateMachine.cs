/// <summary>The gameplay states, independently of the Animator's state names.</summary>
public enum KaoruEnemyState
{
    Idle,
    Chase,
    Attack,
    Hit,
    Death
}

public abstract class KaoruEnemyStateBase
{
    protected readonly KaoruEnemyController Enemy;
    public abstract KaoruEnemyState Id { get; }

    protected KaoruEnemyStateBase(KaoruEnemyController enemy)
    {
        Enemy = enemy;
    }

    public virtual void Enter() { }
    public virtual void Tick() { }
    public virtual void FixedTick() { }
    public virtual void Exit() { }
}

/// <summary>Owns exactly one state; restarting Hit also restarts its animation.</summary>
public sealed class KaoruEnemyStateMachine
{
    public KaoruEnemyStateBase Current { get; private set; }
    public KaoruEnemyStateBase Previous { get; private set; }

    public void ChangeState(KaoruEnemyStateBase next, bool restart = false)
    {
        if (next == null || (!restart && Current == next))
            return;
        Current?.Exit();
        Previous = Current;
        Current = next;
        Current.Enter();
    }

    public void Tick() => Current?.Tick();
    public void FixedTick() => Current?.FixedTick();

    public void Clear()
    {
        Current?.Exit();
        Current = null;
        Previous = null;
    }
}
