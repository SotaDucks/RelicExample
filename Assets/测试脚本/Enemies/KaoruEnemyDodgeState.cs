using UnityEngine;

/// <summary>One side-step per lifetime; normal damage still applies during the animation.</summary>
public sealed class KaoruEnemyDodgeState : KaoruEnemyStateBase
{
    public override KaoruEnemyState Id => KaoruEnemyState.Dodge;
    private Vector3 direction;

    public KaoruEnemyDodgeState(KaoruEnemyController enemy) : base(enemy) { }

    public override void Enter()
    {
        direction = Enemy.GetDodgeDirection();
        Enemy.StopMoving();
        Enemy.PlayAnimation(Id);
    }

    public override void Tick()
    {
        if (Enemy.AnimationReached(1f))
            Enemy.ResumeCombat();
    }

    public override void FixedTick()
    {
        if (Enemy.AnimationReached(Enemy.DodgeMoveEndTime))
            Enemy.StopMoving();
        else
            Enemy.MoveForDodge(direction);
    }

    public override void Exit()
    {
        Enemy.StopMoving();
    }
}
