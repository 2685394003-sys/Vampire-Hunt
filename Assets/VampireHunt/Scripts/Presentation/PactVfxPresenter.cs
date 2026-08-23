using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Infrastructure.Netcode;

namespace VampireHunt.Presentation
{
    public interface IPactVfxDriver
    {
        void Apply(uint pactId, int stacks, Transform anchor);
        void Remove(uint pactId, Transform anchor);
        void Clear(Transform anchor);
    }

    /// <summary>Replicated pact state to VFX adapter. Concrete VFX remains optional.</summary>
    [DisallowMultipleComponent]
    public sealed class PactVfxPresenter : MonoBehaviour
    {
        [SerializeField] private PactNetworkState pactState;
        [SerializeField] private Transform effectAnchor;
        [SerializeField] private MonoBehaviour vfxDriverBehaviour;

        private readonly List<PactStackNetworkState> m_Current = new List<PactStackNetworkState>();
        private readonly Dictionary<uint, int> m_Applied = new Dictionary<uint, int>();
        private IPactVfxDriver m_Driver;

        private void Awake()
        {
            if (pactState == null) pactState = GetComponent<PactNetworkState>();
            if (effectAnchor == null) effectAnchor = transform;
            m_Driver = vfxDriverBehaviour as IPactVfxDriver;
        }

        private void OnEnable()
        {
            if (pactState != null) pactState.InventoryChanged += Reconcile;
            Reconcile();
        }

        private void OnDisable()
        {
            if (pactState != null) pactState.InventoryChanged -= Reconcile;
            m_Driver?.Clear(effectAnchor);
            m_Applied.Clear();
        }

        private void Reconcile()
        {
            if (pactState == null || !pactState.IsSpawned) return;
            pactState.Capture(m_Current);

            var removed = new List<uint>();
            foreach (KeyValuePair<uint, int> pair in m_Applied)
            {
                bool found = false;
                for (int i = 0; i < m_Current.Count; i++)
                    if (m_Current[i].PactId == pair.Key) { found = true; break; }
                if (!found) removed.Add(pair.Key);
            }
            for (int i = 0; i < removed.Count; i++)
            {
                m_Driver?.Remove(removed[i], effectAnchor);
                m_Applied.Remove(removed[i]);
            }

            for (int i = 0; i < m_Current.Count; i++)
            {
                PactStackNetworkState pact = m_Current[i];
                if (m_Applied.TryGetValue(pact.PactId, out int stacks) && stacks == pact.Stacks) continue;
                m_Applied[pact.PactId] = pact.Stacks;
                m_Driver?.Apply(pact.PactId, pact.Stacks, effectAnchor);
            }
        }
    }
}
