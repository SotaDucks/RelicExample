public sealed class KaoruEnemyIdleState : KaoruEnemyStateBase
{
    public override KaoruEnemyState Id => KaoruEnemyState.Idle;
    private float engagementRange;

    public KaoruEnemyIdleState(KaoruEnemyController enemy) : base(enemy) { }

    public override void Enter()
    {
        engagementRange = Enemy.IsReturningFromMelee ? Enemy.AttackExitRange : Enemy.AttackRange;
        Enemy.StopMoving();
        Enemy.PlayAnimation(Id);
    }

    public override void Tick()
    {
        if (!Enemy.TargetIsWithinPursuitRange(Enemy.DetectionRadius))
            return;
        if (!Enemy.TargetIsWithin(engagementRange))
            Enemy.ChangeState(KaoruEnemyState.Chase);
        else if (Enemy.CanAttack)
            Enemy.ChangeState(KaoruEnemyState.Attack);
    }

    public override void FixedTick()
    {
        Enemy.StopMoving();
        if (Enemy.TargetIsWithin(Enemy.AttackExitRange))
            Enemy.FaceTarget();
    }
}
