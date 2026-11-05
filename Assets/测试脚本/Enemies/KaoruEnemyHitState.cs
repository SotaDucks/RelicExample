public sealed class KaoruEnemyHitState : KaoruEnemyStateBase
{
    public override KaoruEnemyState Id => KaoruEnemyState.Hit;

    public KaoruEnemyHitState(KaoruEnemyController enemy) : base(enemy) { }

    public override void Enter()
    {
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
        Enemy.StopMoving();
    }
}
