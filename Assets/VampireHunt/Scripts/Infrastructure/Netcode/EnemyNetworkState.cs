using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Enemies;

namespace VampireHunt.Infrastructure.Netcode
{
    [Serializable]
    public struct EnemyNetworkState : INetworkSerializable, IEquatable<EnemyNetworkState>
    {
        public ulong EntityId;
        public EnemyState State;
        public float CurrentHealth;
        public float MaxHealth;
        public double StateEndServerTime;
        public uint AttackSequence;
        public Vector3 AttackAimDirection;
        public uint Revision;

        public static EnemyNetworkState FromSnapshot(
            in EnemySnapshot snapshot,
            Vector3 attackAimDirection = default)
        {
            return new EnemyNetworkState
            {
                EntityId = snapshot.EntityId.Value,
                State = snapshot.State,
                CurrentHealth = snapshot.CurrentHealth,
                MaxHealth = snapshot.MaxHealth,
                StateEndServerTime = snapshot.StateEndTime,
                AttackSequence = snapshot.AttackSequence,
                AttackAimDirection = attackAimDirection,
                Revision = snapshot.Revision
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref EntityId);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref CurrentHealth);
            serializer.SerializeValue(ref MaxHealth);
            serializer.SerializeValue(ref StateEndServerTime);
            serializer.SerializeValue(ref AttackSequence);
            serializer.SerializeValue(ref AttackAimDirection);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(EnemyNetworkState other)
        {
            return EntityId == other.EntityId &&
                   State == other.State &&
                   CurrentHealth.Equals(other.CurrentHealth) &&
                   MaxHealth.Equals(other.MaxHealth) &&
                   StateEndServerTime.Equals(other.StateEndServerTime) &&
                   AttackSequence == other.AttackSequence &&
                   AttackAimDirection.Equals(other.AttackAimDirection) &&
                   Revision == other.Revision;
        }
    }
}
