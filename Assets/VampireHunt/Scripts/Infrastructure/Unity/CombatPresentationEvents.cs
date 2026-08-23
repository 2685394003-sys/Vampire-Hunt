using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity
{
    [System.Serializable]
    public struct DamagePresentationPayload
    {
        public ulong sourceEntityId;
        public ulong targetEntityId;
        public uint attackId;
        public ulong sequence;
        public float amount;
        public DamageTags tags;
        public Vector3 worldPosition;
    }

    [System.Serializable]
    public struct StatusEffectPresentationPayload
    {
        public ulong sourceEntityId;
        public ulong targetEntityId;
        public uint statusId;
        public int stacks;
        public float magnitude;
        public double startServerTime;
        public double endServerTime;
        public EffectBlockFlags blockFlags;
        public uint presentationCueId;
        public ElementId element;
        public Vector3 worldPosition;
    }
}
