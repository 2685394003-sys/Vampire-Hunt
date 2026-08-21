using System;
using System.IO;
using NUnit.Framework;

namespace VampireHunt.Tests.Player
{
    public sealed class PlayerPosePrefabContractTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Network/Player.prefab";
        private const string PoseAdapterPath =
            "Assets/Scripts/VampireHunt/Infrastructure/Netcode/PlayerPoseNetworkAdapter.cs";

        [Test]
        public void PlayerPrefab_UsesOwnerAuthority_AndPreservesNetworkIdentity()
        {
            string prefab = File.ReadAllText(PlayerPrefabPath);

            Assert.That(Count(prefab, "Unity.Netcode.Runtime::Unity.Netcode.NetworkObject"), Is.EqualTo(1));
            Assert.That(Count(prefab, "Unity.Netcode.Runtime::Unity.Netcode.Components.NetworkTransform"), Is.EqualTo(1));
            Assert.That(Count(prefab, "Unity.Netcode.Runtime::Unity.Netcode.Components.NetworkAnimator"), Is.EqualTo(1));
            Assert.That(prefab, Does.Contain("GlobalObjectIdHash: 2983054044"));
            Assert.That(prefab, Does.Contain("m_Script: {fileID: 11500000, guid: d5a57f767e5e46a458fc5d3c628d0cbb, type: 3}"));
            Assert.That(ComponentField(prefab, "Unity.Netcode.Runtime::Unity.Netcode.Components.NetworkTransform", "AuthorityMode"),
                Is.EqualTo("1"));
            // NetworkAnimator is intentionally outside this migration slice.
            Assert.That(ComponentField(prefab, "Unity.Netcode.Runtime::Unity.Netcode.Components.NetworkAnimator", "AuthorityMode"),
                Is.EqualTo("0"));
        }

        [Test]
        public void PoseAdapter_RecordsOwnerPoseWithoutCorrectionRpc()
        {
            string adapter = File.ReadAllText(PoseAdapterPath);

            Assert.That(adapter, Does.Contain("runtime.SubmitPose(pose)"));
            Assert.That(adapter, Does.Not.Contain("MovementVerdict"));
            Assert.That(adapter, Does.Not.Contain("CorrectOwnerPoseRpc"));
            Assert.That(adapter, Does.Not.Contain("NetworkTransform.Teleport"));
            Assert.That(adapter, Does.Not.Contain("RpcTarget.Single"));
        }

        private static int Count(string source, string value)
        {
            int count = 0;
            int offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }

        private static string ComponentField(string prefab, string component, string field)
        {
            int componentStart = prefab.IndexOf(component, StringComparison.Ordinal);
            Assert.That(componentStart, Is.GreaterThanOrEqualTo(0), $"Missing component {component}");
            int nextComponent = prefab.IndexOf("m_EditorClassIdentifier:", componentStart + component.Length,
                StringComparison.Ordinal);
            int end = nextComponent >= 0 ? nextComponent : prefab.Length;
            string block = prefab.Substring(componentStart, end - componentStart);
            string prefix = $"  {field}: ";
            int fieldStart = block.IndexOf(prefix, StringComparison.Ordinal);
            Assert.That(fieldStart, Is.GreaterThanOrEqualTo(0), $"Missing {field} in {component}");
            int valueStart = fieldStart + prefix.Length;
            int valueEnd = block.IndexOf('\n', valueStart);
            if (valueEnd < 0) valueEnd = block.Length;
            return block.Substring(valueStart, valueEnd - valueStart).Trim();
        }
    }
}
