using UnityEngine;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Player.Authoring
{
    /// <summary>Single authored Blood Pact entry converted to a small immutable option.</summary>
    [CreateAssetMenu(
        fileName = "BloodPactDefinition",
        menuName = "Vampire Hunt/Player/Blood Pact Definition",
        order = 2)]
    public sealed class BloodPactDefinition : ScriptableObject
    {
        [SerializeField] private string pactId;
        [SerializeField] private int cost;
        [SerializeField] private bool repeatable = true;
        [SerializeField] private int maximumStacks = 99;

        public BloodPactId Id => new(pactId);
        public int Cost => Mathf.Max(0, cost);
        public bool Repeatable => repeatable;
        public int MaximumStacks => Mathf.Max(1, maximumStacks);

        public BloodPactOption CreateOption() =>
            new(Id, Cost, Repeatable, MaximumStacks);

        private void OnValidate()
        {
            cost = Mathf.Max(0, cost);
            maximumStacks = Mathf.Max(1, maximumStacks);
        }
    }
}
