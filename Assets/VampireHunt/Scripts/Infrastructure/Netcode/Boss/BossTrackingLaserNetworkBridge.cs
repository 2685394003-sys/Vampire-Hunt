using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    public readonly struct BossTrackingLaserPresentation
    {
        public uint AbilityId { get; }
        public ulong CastSequence { get; }
        public double StartServerTime { get; }
        public ulong TargetEntityId { get; }
        public Vector3 InitialDirection { get; }
        public Vector3 Size { get; }
        public float RotationSpeed { get; }

        public BossTrackingLaserPresentation(
            uint abilityId,
            ulong castSequence,
            double startServerTime,
            ulong targetEntityId,
            Vector3 initialDirection,
            Vector3 size,
            float rotationSpeed)
        {
            AbilityId = abilityId;
            CastSequence = castSequence;
            StartServerTime = startServerTime;
            TargetEntityId = targetEntityId;
            InitialDirection = initialDirection.sqrMagnitude > .0001f
                ? initialDirection.normalized
                : Vector3.forward;
            Size = new Vector3(
                Mathf.Max(.01f, size.x),
                Mathf.Max(.01f, size.y),
                Mathf.Max(.01f, size.z));
            RotationSpeed = Mathf.Max(0f, rotationSpeed);
        }
    }

    /// <summary>
    /// Replicates the immutable lock-on snapshot. Damage and target selection stay server-only;
    /// this bridge only tells every client which synchronized player to visually follow.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossTrackingLaserNetworkBridge : NetworkBehaviour,
        IBossTrackingLaserPresentationService
    {
        public event Action<BossTrackingLaserPresentation> LaserStarted;
        public event Action<uint, ulong> LaserCancelled;

        public bool TryPublish(in BossTrackingLaserPresentationRequest request)
        {
            if (!IsSpawned || !IsServer || request.TargetEntityId == GameplayEntityId.None) return false;
            PublishLaserRpc(
                request.AbilityId,
                request.CastSequence,
                request.StartServerTime,
                request.TargetEntityId.Value,
                ToVector3(request.InitialDirection),
                ToVector3(request.Size),
                request.RotationSpeed);
            return true;
        }

        public bool TryCancel(uint abilityId, ulong castSequence)
        {
            if (!IsSpawned || !IsServer || castSequence == 0) return false;
            CancelLaserRpc(abilityId, castSequence);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void PublishLaserRpc(
            uint abilityId,
            ulong castSequence,
            double startServerTime,
            ulong targetEntityId,
            Vector3 initialDirection,
            Vector3 size,
            float rotationSpeed)
        {
            LaserStarted?.Invoke(new BossTrackingLaserPresentation(
                abilityId,
                castSequence,
                startServerTime,
                targetEntityId,
                initialDirection,
                size,
                rotationSpeed));
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void CancelLaserRpc(uint abilityId, ulong castSequence)
        {
            LaserCancelled?.Invoke(abilityId, castSequence);
        }

        private static Vector3 ToVector3(in Float3 value) =>
            new Vector3(value.X, value.Y, value.Z);
    }
}
