using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>Shared art references only; status rules remain in the combat catalog.</summary>
    [CreateAssetMenu(menuName = "Vampire Hunt/Presentation/Status Mesh VFX", fileName = "StatusMeshVfxConfig")]
    public sealed class StatusMeshVfxConfig : ScriptableObject
    {
        [SerializeField] private GameObject burnPrefab;
        [SerializeField] private GameObject frozenPrefab;

        public GameObject GetPrefab(uint statusId)
        {
            switch (statusId)
            {
                case StatusEffectIds.Burn: return burnPrefab;
                case StatusEffectIds.Frozen: return frozenPrefab;
                default: return null;
            }
        }
    }
}
