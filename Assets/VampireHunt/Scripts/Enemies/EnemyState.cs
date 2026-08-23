namespace VampireHunt.Enemies
{
    public enum EnemyState : byte
    {
        Spawning = 0,
        Seeking = 1,
        Approaching = 2,
        Orbiting = 3,
        Telegraphing = 4,
        Attacking = 5,
        Recovering = 6,
        Stunned = 7,
        Dead = 8
    }
}
