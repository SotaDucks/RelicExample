public sealed class KaoruEnemyAttackState : KaoruEnemyStateBase
{
    public override KaoruEnemyState Id => KaoruEnemyState.Attack;
    private bool hitChecked;

    public KaoruEnemyAttackState(KaoruEnemyController enemy) : base(enemy) { }

    public override void Enter()
    {
        hitChecked = false;
        Enemy.StopMoving();
        Enemy.PlayAnimation(Id);
    }

    public override void Tick()
    {
        if (!hitChecked && Enemy.AnimationReached(Enemy.AttackHitTime))
        {
            // One check per swing, even when the player dodges or has several colliders.
            hitChecked = true;
            Enemy.TryHitTarget();
        }
        // A damage callback may have interrupted this attack with Hit or Death.
        if (Enemy.CurrentState != Id)
            return;
        if (Enemy.AnimationReached(1f))
            Enemy.ResumeCombat();
    }

    public override void FixedTick()
    {
        Enemy.StopMoving();
        if (!hitChecked)
            Enemy.FaceTarget();
    }

    public override void Exit()
    {
        Enemy.StartAttackCooldown();
    }
}
