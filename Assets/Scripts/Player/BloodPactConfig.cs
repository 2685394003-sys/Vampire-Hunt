using System.Collections.Generic;
using UnityEngine;

/// <summary>Design database for all numeric blood-pact definitions.</summary>
[CreateAssetMenu(
    fileName = "BloodPacts",
    menuName = "Vampire Hunt/Balance/Blood Pact Database",
    order = 3)]
public sealed class BloodPactConfig : ScriptableObject
{
    private const string DefaultResourcePath = "GameBalance/BloodPacts";

    [SerializeField] private List<BloodPactDefinition> pacts = new();

    public IReadOnlyList<BloodPactDefinition> Pacts => pacts;

    public bool TryGet(string pactId, out BloodPactDefinition result)
    {
        foreach (BloodPactDefinition pact in pacts)
        {
            if (pact != null && pact.PactId == pactId)
            {
                result = pact;
                return true;
            }
        }

        result = null;
        return false;
    }

    public bool TryApplyEffects(string pactId, PlayerNetworkState player)
    {
        return TryGet(pactId, out BloodPactDefinition pact) &&
               pact.ApplyEffects(player);
    }

    public bool TryApplyNumericEffects(string pactId, PlayerNetworkState player) =>
        TryApplyEffects(pactId, player);

    public static BloodPactConfig LoadDefault() =>
        Resources.Load<BloodPactConfig>(DefaultResourcePath);

    /// <summary>
    /// Call once from the run coordinator when starting a new run. Each player
    /// still clears its own modifiers through PlayerNetworkState.ResetForNewRun.
    /// </summary>
    public static void ResetEnemyNumericEffects() => EnemyRunStats.ResetForNewRun();
}
