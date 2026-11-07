public sealed class KaoruEnemyChaseState : KaoruEnemyStateBase
{
    public override KaoruEnemyState Id => KaoruEnemyState.Chase;

    public KaoruEnemyChaseState(KaoruEnemyController enemy) : base(enemy) { }

    public override void Enter()
    {
        Enemy.PlayAnimation(Id);
    }

    public override void Tick()
    {
        if (!Enemy.TargetIsWithinPursuitRange(Enemy.LoseTargetRadius))
            Enemy.ChangeState(KaoruEnemyState.Idle);
        else if (Enemy.TargetIsWithin(Enemy.AttackRange))
            Enemy.ChangeState(Enemy.CanAttack ? KaoruEnemyState.Attack : KaoruEnemyState.Idle);
    }

    public override void FixedTick()
    {
        Enemy.MoveTowardTarget();
    }
}
