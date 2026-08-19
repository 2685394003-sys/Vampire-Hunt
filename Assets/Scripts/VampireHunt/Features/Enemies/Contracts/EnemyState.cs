namespace VampireHunt.Enemies.Contracts
{
    public enum EnemyState
    {
        Idle = 0,
        Chasing = 1,
        Attacking = 2,
        Stunned = 3,
        Recovering = 4,
        Dead = 5
    }

    public enum EnemyAttackType
    {
        Melee = 0,
        Ranged = 1
    }
}
