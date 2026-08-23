using VampireHunt.Contracts;

namespace VampireHunt.Enemies
{
    /// <summary>
    /// Application boundary used by the network actor. Unity physics, targeting
    /// and presentation stay outside this service.
    /// </summary>
    public sealed class EnemyApplicationService
    {
        public EnemyTickResult Tick(EnemyAggregate enemy, in EnemyTickInput input)
        {
            return enemy.Tick(input);
        }

        public bool ApplyDamage(EnemyAggregate enemy, in ResolvedDamage damage)
        {
            return enemy.ApplyDamage(damage);
        }
    }
}
