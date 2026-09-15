using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    public abstract class EffectModuleAsset : ScriptableObject
    {
        public abstract IEffectModuleDescriptor ToDescriptor();
    }
}
