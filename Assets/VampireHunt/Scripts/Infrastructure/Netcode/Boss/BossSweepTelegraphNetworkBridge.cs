using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    public readonly struct BossSweepPresentationPass
    {
        public uint PassIndex { get; }
        public double StartServerTime { get; }
        public Vector3 Center { get; }
        public Vector3 Forward { get; }
        public Vector3 Size { get; }
        public BossSweepDirection Direction { get; }

        public BossSweepPresentationPass(
            uint passIndex,
            double startServerTime,
            Vector3 center,
            Vector3 forward,
            Vector3 size,
            BossSweepDirection direction)
        {
            PassIndex = passIndex;
            StartServerTime = startServerTime;
            Center = center;
            Forward = forward.sqrMagnitude > .0001f ? forward.normalized : Vector3.forward;
            Size = new Vector3(
                Mathf.Max(.01f, size.x),
                Mathf.Max(.01f, size.y),
                Mathf.Max(.01f, size.z));
            Direction = direction;
        }
    }

    /// <summary>Replicates all passes of one sweep cast as one immutable server snapshot.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossSweepTelegraphNetworkBridge : NetworkBehaviour, IBossSweepTelegraphService
    {
        public event Action<uint, ulong, BossSweepPresentationPass[]> SweepStarted;
        public event Action<uint, ulong, uint> SweepPassCancelled;
        public event Action<uint, ulong> SweepCancelled;

        public bool TryPublish(in BossSweepTelegraphRequest request)
        {
            if (!IsSpawned || !IsServer || request.Passes == null || request.Passes.Length == 0)
                return false;

            int count = request.Passes.Length;
            var passIndices = new uint[count];
            var startTimes = new double[count];
            var centers = new Vector3[count];
            var forwards = new Vector3[count];
            var sizes = new Vector3[count];
            var directions = new byte[count];
            for (int i = 0; i < count; i++)
            {
                BossSweepTelegraphPass pass = request.Passes[i];
                passIndices[i] = pass.PassIndex;
                startTimes[i] = pass.StartServerTime;
                centers[i] = ToVector3(pass.Center);
                forwards[i] = ToVector3(pass.Forward);
                sizes[i] = ToVector3(pass.Size);
                directions[i] = (byte)pass.Direction;
            }

            PublishSweepRpc(
                request.AbilityId,
                request.CastSequence,
                passIndices,
                startTimes,
                centers,
                forwards,
                sizes,
                directions);
            return true;
        }

        public bool TryCancelPass(uint abilityId, ulong castSequence, uint passIndex)
        {
            if (!IsSpawned || !IsServer || castSequence == 0) return false;
            CancelSweepPassRpc(abilityId, castSequence, passIndex);
            return true;
        }

        public bool TryCancel(uint abilityId, ulong castSequence)
        {
            if (!IsSpawned || !IsServer || castSequence == 0) return false;
            CancelSweepRpc(abilityId, castSequence);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void PublishSweepRpc(
            uint abilityId,
            ulong castSequence,
            uint[] passIndices,
            double[] startTimes,
            Vector3[] centers,
            Vector3[] forwards,
            Vector3[] sizes,
            byte[] directions)
        {
            int count = MinLength(passIndices, startTimes, centers, forwards, sizes, directions);
            if (count <= 0) return;
            var passes = new BossSweepPresentationPass[count];
            for (int i = 0; i < count; i++)
            {
                passes[i] = new BossSweepPresentationPass(
                    passIndices[i],
                    startTimes[i],
                    centers[i],
                    forwards[i],
                    sizes[i],
                    (BossSweepDirection)directions[i]);
            }
            SweepStarted?.Invoke(abilityId, castSequence, passes);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void CancelSweepPassRpc(uint abilityId, ulong castSequence, uint passIndex)
        {
            SweepPassCancelled?.Invoke(abilityId, castSequence, passIndex);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        private void CancelSweepRpc(uint abilityId, ulong castSequence)
        {
            SweepCancelled?.Invoke(abilityId, castSequence);
        }

        private static int MinLength(
            uint[] passIndices,
            double[] startTimes,
            Vector3[] centers,
            Vector3[] forwards,
            Vector3[] sizes,
            byte[] directions)
        {
            if (passIndices == null || startTimes == null || centers == null || forwards == null ||
                sizes == null || directions == null) return 0;
            return Math.Min(
                Math.Min(Math.Min(passIndices.Length, startTimes.Length), Math.Min(centers.Length, forwards.Length)),
                Math.Min(sizes.Length, directions.Length));
        }

        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
