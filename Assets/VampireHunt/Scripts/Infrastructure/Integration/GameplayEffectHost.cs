using Unity.Netcode;
using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Single modular effect runtime owned by one network entity.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class GameplayEffectHost : MonoBehaviour
    {
        private readonly EffectPortCollection m_Ports = new EffectPortCollection();
        private EffectRuntimeHost m_Runtime;
        private NetworkObject m_NetworkObject;

        public int SourceCount => m_Runtime?.SourceCount ?? 0;

        private void Awake()
        {
            m_NetworkObject = GetComponent<NetworkObject>();
            m_Runtime = new EffectRuntimeHost(BuiltInEffectModuleFactories.CreateRegistry());
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++) m_Ports.Add(behaviours[i]);
        }

        private void Update()
        {
            if (m_Runtime == null) return;
            NetworkManager manager = m_NetworkObject != null ? m_NetworkObject.NetworkManager : null;
            double time = manager != null && manager.IsListening
                ? manager.ServerTime.Time
                : Time.unscaledTimeAsDouble;
            m_Runtime.Tick(time);
        }

        public void SetSource(
            EffectDefinition definition,
            in EffectRuntimeState state,
            IEffectCommandSink commands = null)
        {
            var context = new EffectRuntimeContext(ResolveRealm(), m_Ports, commands);
            m_Runtime.SetSource(definition, state, context);
        }

        public bool RemoveSource(in EffectSourceKey key) => m_Runtime != null && m_Runtime.RemoveSource(key);
        public void RemoveSourceKind(EffectSourceKind kind) => m_Runtime?.RemoveSourceKind(kind);
        public bool HasBlock(EffectBlockFlags flags) => m_Runtime != null && m_Runtime.HasBlock(flags);

        private EffectExecutionRealm ResolveRealm()
        {
            EffectExecutionRealm realm = EffectExecutionRealm.Presentation;
            if (m_NetworkObject != null && m_NetworkObject.IsSpawned)
            {
                NetworkManager manager = m_NetworkObject.NetworkManager;
                if (manager != null && manager.IsServer) realm |= EffectExecutionRealm.Server;
                if (m_NetworkObject.IsOwner) realm |= EffectExecutionRealm.Owner;
            }
            return realm;
        }

        private void OnDestroy()
        {
            m_Runtime?.Dispose();
            m_Runtime = null;
        }
    }
}
