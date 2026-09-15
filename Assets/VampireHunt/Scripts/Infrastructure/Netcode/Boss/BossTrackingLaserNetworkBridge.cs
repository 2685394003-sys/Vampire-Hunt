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
        public uint BeamIndex { get; }
        public double StartServerTime { get; }
        public ulong TargetEntityId { get; }
        public Vector3 InitialDirection { get; }
        public Vector3 Size { get; }
        public float RotationSpeed { get; }
        public double TelegraphDuration { get; }

        public BossTrackingLaserPresentation(
            uint abilityId,
            ulong castSequence,
            uint beamIndex,
            double startServerTime,
            ulong targetEntityId,
            Vector3 initialDirection,
            Vector3 size,
            float rotationSpeed,
            double telegraphDuration)
        {
            AbilityId = abilityId;
            CastSequence = castSequence;
            BeamIndex = beamIndex;
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
            TelegraphDuration = Math.Max(0d, telegraphDuration);
        }
    }

    /// <summary>
    /// Replicates one start snapshot per beam plus rate-limited authoritative directions.
    /// Damage, target selection and turning decisions remain server-only.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossTrackingLaserNetworkBridge : NetworkBehaviour,
        IBossTrackingLaserPresentationService
    {
        public event Action<BossTrackingLaserPresentation> LaserStarted;
        public event Action<uint, ulong, uint, Vector3, bool> LaserDirectionUpdated;
        public event Action<uint, ulong> LaserCancelled;

        public bool TryPublish(in BossTrackingLaserPresentationRequest request)
        {
            if (!IsSpawned || !IsServer || request.TargetEntityId == GameplayEntityId.None) return false;
            PublishLaserRpc(
                request.AbilityId,
                request.CastSequence,
                request.BeamIndex,
                request.StartServerTime,
                request.TargetEntityId.Value,
                ToVector3(request.InitialDirection),
                ToVector3(request.Size),
                request.RotationSpeed,
                request.TelegraphDuration);
            return true;
        }

        public bool TryUpdate(
            uint abilityId,
            ulong castSequence,
            uint beamIndex,
            in Float3 direction,
            bool snap)
        {
            if (!IsSpawned || !IsServer || castSequence == 0) return false;
            UpdateLaserDirectionRpc(
                abilityId, castSequence, beamIndex, ToVector3(direction), snap);
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
            uint beamIndex,
            double startServerTime,
            ulong targetEntityId,
            Vector3 initialDirection,
            Vector3 size,
            float rotationSpeed,
            double telegraphDuration)
        {
            LaserStarted?.Invoke(new BossTrackingLaserPresentation(
                abilityId,
                castSequence,
                beamIndex,
                startServerTime,
                targetEntityId,
                initialDirection,
                size,
                rotationSpeed,
                telegraphDuration));
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void UpdateLaserDirectionRpc(
            uint abilityId,
            ulong castSequence,
            uint beamIndex,
            Vector3 direction,
            bool snap)
        {
            Vector3 normalized = direction.sqrMagnitude > .0001f
                ? direction.normalized
                : Vector3.forward;
            LaserDirectionUpdated?.Invoke(
                abilityId, castSequence, beamIndex, normalized, snap);
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
