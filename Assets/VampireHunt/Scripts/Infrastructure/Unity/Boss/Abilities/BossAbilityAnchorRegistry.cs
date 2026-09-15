using System;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    [DisallowMultipleComponent]
    public sealed class BossAbilityAnchorRegistry : MonoBehaviour
    {
        [Serializable]
        public sealed class Binding
        {
            [SerializeField] private BossAbilityAnchorId id;
            [SerializeField] private Transform anchor;

            public BossAbilityAnchorId Id => id;
            public Transform Anchor => anchor;

            public Binding(BossAbilityAnchorId id, Transform anchor)
            {
                this.id = id;
                this.anchor = anchor;
            }
        }

        [SerializeField] private Binding[] bindings = Array.Empty<Binding>();

        public Transform Resolve(BossAbilityAnchorId id)
        {
            if (id == BossAbilityAnchorId.Root) return transform;
            for (int i = 0; i < bindings.Length; i++)
            {
                Binding binding = bindings[i];
                if (binding != null && binding.Id == id && binding.Anchor != null)
                    return binding.Anchor;
            }
            return transform;
        }

#if UNITY_EDITOR
        public void EditorSetBindings(Binding[] values)
        {
            bindings = values ?? Array.Empty<Binding>();
        }
#endif
    }
}
