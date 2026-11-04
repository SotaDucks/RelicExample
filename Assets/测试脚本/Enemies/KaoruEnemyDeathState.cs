using UnityEngine;

public sealed class KaoruEnemyDeathState : KaoruEnemyStateBase
{
    public override KaoruEnemyState Id => KaoruEnemyState.Death;
    private float removeAt;
    private bool animationFinished;

    public KaoruEnemyDeathState(KaoruEnemyController enemy) : base(enemy) { }

    public override void Enter()
    {
        animationFinished = false;
        removeAt = float.PositiveInfinity;
        Enemy.FreezeForDeath();
        Enemy.PlayAnimation(Id);
    }

    public override void Tick()
    {
        if (!animationFinished && Enemy.AnimationReached(1f))
        {
            animationFinished = true;
            removeAt = Time.time + Enemy.CorpseLifetime;
        }
        if (Enemy.DespawnAfterDeath && animationFinished && Time.time >= removeAt)
            Object.Destroy(Enemy.gameObject);
    }
}
