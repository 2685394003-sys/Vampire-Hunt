using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    public readonly struct BossAreaTelegraphPresentation
    {
        public uint AbilityId { get; }
        public ulong CastSequence { get; }
        public double StartServerTime { get; }
        public BossAreaTelegraphShape Shape { get; }
        public float Radius { get; }
        public Vector3 Forward { get; }
        public Vector3 Size { get; }
        public Vector3[] Centers { get; }
        public double TelegraphDuration { get; }

        public BossAreaTelegraphPresentation(
            uint abilityId,
            ulong castSequence,
            double startServerTime,
            BossAreaTelegraphShape shape,
            float radius,
            Vector3 forward,
            Vector3 size,
            Vector3[] centers,
            double telegraphDuration)
        {
            AbilityId = abilityId;
            CastSequence = castSequence;
            StartServerTime = startServerTime;
            Shape = shape;
            Radius = Mathf.Max(0f, radius);
            Forward = forward.sqrMagnitude > .0001f ? forward.normalized : Vector3.forward;
            Size = new Vector3(
                Mathf.Max(.01f, size.x),
                Mathf.Max(.01f, size.y),
                Mathf.Max(.01f, size.z));
            Centers = centers ?? Array.Empty<Vector3>();
            TelegraphDuration = Math.Max(0d, telegraphDuration);
        }
    }

    /// <summary>
    /// Atomic server-to-client adapter for fixed world-space area warnings.
    /// It owns no targeting, damage, timing decisions or VFX instantiation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossAreaTelegraphNetworkBridge : NetworkBehaviour, IBossAreaTelegraphService
    {
        public event Action<BossAreaTelegraphPresentation> TelegraphStarted;
        public event Action<uint, ulong> TelegraphCancelled;

        public bool TryPublish(in BossAreaTelegraphRequest request)
        {
            if (!IsSpawned || !IsServer || request.Centers == null || request.Centers.Length == 0)
                return false;
            var centers = new Vector3[request.Centers.Length];
            for (int i = 0; i < centers.Length; i++)
            {
                var point = request.Centers[i];
                centers[i] = new Vector3(point.X, point.Y, point.Z);
            }

            PublishTelegraphRpc(request.AbilityId, request.CastSequence, request.StartServerTime,
                (byte)request.Shape,
                request.Radius,
                new Vector3(request.Forward.X, request.Forward.Y, request.Forward.Z),
                new Vector3(request.Size.X, request.Size.Y, request.Size.Z),
                centers,
                request.TelegraphDuration);
            return true;
        }

        public bool TryCancel(uint abilityId, ulong castSequence)
        {
            if (!IsSpawned || !IsServer || castSequence == 0) return false;
            CancelTelegraphRpc(abilityId, castSequence);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void PublishTelegraphRpc(uint abilityId, ulong castSequence, double startServerTime,
            byte shape, float radius, Vector3 forward, Vector3 size, Vector3[] centers,
            double telegraphDuration)
        {
            TelegraphStarted?.Invoke(new BossAreaTelegraphPresentation(
                abilityId,
                castSequence,
                startServerTime,
                (BossAreaTelegraphShape)shape,
                radius,
                forward,
                size,
                centers,
                telegraphDuration));
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void CancelTelegraphRpc(uint abilityId, ulong castSequence)
        {
            TelegraphCancelled?.Invoke(abilityId, castSequence);
        }
    }
}
