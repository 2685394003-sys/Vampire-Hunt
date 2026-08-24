using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Boss.Abilities;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    public enum BossAbilityAnchorId : byte
    {
        Root = 0,
        Chest = 1,
        Head = 2,
        LeftHand = 3,
        RightHand = 4,
        Ground = 5,
        Target = 6
    }

    [Serializable]
    public sealed class BossAbilityPresentationCue
    {
        [Min(0f)] [SerializeField] private float timeFromCastStart;
        [SerializeField] private string cueName = "Cue";
        [SerializeField] private BossAbilityAnchorId anchor = BossAbilityAnchorId.Root;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField] private bool followAnchor = true;
        [Min(0f)] [SerializeField] private float lifetime = 1f;

        [Header("Presentation")]
        [SerializeField] private string animatorTrigger;
        [SerializeField] private GameObject vfxPrefab;
        [SerializeField] private AudioClip audioClip;
        [Range(0f, 1f)] [SerializeField] private float audioVolume = 1f;

        public float TimeFromCastStart => timeFromCastStart;
        public string CueName => cueName;
        public BossAbilityAnchorId Anchor => anchor;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;
        public bool FollowAnchor => followAnchor;
        public float Lifetime => lifetime;
        public string AnimatorTrigger => animatorTrigger;
        public GameObject VfxPrefab => vfxPrefab;
        public AudioClip AudioClip => audioClip;
        public float AudioVolume => audioVolume;
    }

    internal static class BossAbilityLogicTypeResolver
    {
        public static Func<IBossAbilityLogicRuntime> CreateFactory(string assemblyQualifiedTypeName)
        {
            if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
                throw new InvalidOperationException("The Boss Ability has no Logic script assigned.");

            Type logicType = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
            if (logicType == null)
            {
                string fullTypeName = assemblyQualifiedTypeName.Split(',')[0].Trim();
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    logicType = assembly.GetType(fullTypeName, throwOnError: false);
                    if (logicType != null) break;
                }
            }

            if (logicType == null)
                throw new InvalidOperationException(
                    $"Boss Ability Logic type could not be loaded: {assemblyQualifiedTypeName}");
            if (!typeof(IBossAbilityLogicRuntime).IsAssignableFrom(logicType) ||
                logicType.IsAbstract || logicType.IsInterface)
                throw new InvalidOperationException(
                    $"{logicType.FullName} must be a concrete {nameof(IBossAbilityLogicRuntime)} script.");
            if (logicType.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException(
                    $"{logicType.FullName} must have a public parameterless constructor.");

            return () => (IBossAbilityLogicRuntime)Activator.CreateInstance(logicType);
        }
    }

    [CreateAssetMenu(fileName = "BossAbility", menuName = "Vampire Hunt/Boss/Ability")]
    public sealed class BossAbilityAsset : ScriptableObject
    {
        [Header("Identity")]
        [Min(1)] [SerializeField] private uint abilityId = 1;
        [SerializeField] private string displayName = "New Boss Ability";
        [TextArea] [SerializeField] private string description;
        [SerializeField] private Sprite icon;

        [Header("Selection")]
        [Min(0.0001f)] [SerializeField] private float baseWeight = 1f;
        [Min(0f)] [SerializeField] private float cooldown = 1f;
        [Min(0f)] [SerializeField] private float minDistance;
        [Min(0f)] [SerializeField] private float maxDistance = 100f;
        [Range(0f, 1f)] [SerializeField] private float minNormalizedHealth;
        [Range(0f, 1f)] [SerializeField] private float maxNormalizedHealth = 1f;
        [SerializeField] private bool requiresTarget;
        [SerializeField] private bool oneShot;

        [Header("Server Timeline")]
        [Min(0f)] [SerializeField] private float telegraphDuration = 0.5f;
        [Min(0.01f)] [SerializeField] private float resolveDuration = 0.1f;
        [Min(0f)] [SerializeField] private float recoverDuration = 0.5f;

        [Header("Gameplay Logic")]
        [Tooltip("The .cs script that implements this ability's server-side behavior.")]
        [HideInInspector] [SerializeField] private UnityEngine.Object logicScript;
        [HideInInspector] [SerializeField] private string logicTypeName;

        [Header("Client Presentation Timeline")]
        [SerializeField] private BossAbilityPresentationCue[] presentationCues =
            Array.Empty<BossAbilityPresentationCue>();

        public uint AbilityId => abilityId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public UnityEngine.Object LogicScript => logicScript;
        public string LogicTypeName => logicTypeName;
        public float TotalDuration => telegraphDuration + resolveDuration + recoverDuration;
        public IReadOnlyList<BossAbilityPresentationCue> PresentationCues => presentationCues;

        public BossAbilityDefinition CreateDefinition()
        {
            return new BossAbilityDefinition(
                abilityId,
                displayName,
                baseWeight,
                cooldown,
                minDistance,
                maxDistance,
                minNormalizedHealth,
                maxNormalizedHealth,
                requiresTarget,
                oneShot,
                telegraphDuration,
                resolveDuration,
                recoverDuration,
                BossAbilityLogicTypeResolver.CreateFactory(logicTypeName));
        }

        private void OnValidate()
        {
            if (abilityId == 0) abilityId = 1;
            baseWeight = Mathf.Max(0.0001f, baseWeight);
            cooldown = Mathf.Max(0f, cooldown);
            minDistance = Mathf.Max(0f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            minNormalizedHealth = Mathf.Clamp01(minNormalizedHealth);
            maxNormalizedHealth = Mathf.Clamp(maxNormalizedHealth, minNormalizedHealth, 1f);
            telegraphDuration = Mathf.Max(0f, telegraphDuration);
            resolveDuration = Mathf.Max(0.01f, resolveDuration);
            recoverDuration = Mathf.Max(0f, recoverDuration);
            presentationCues ??= Array.Empty<BossAbilityPresentationCue>();
        }
    }

    [Serializable]
    public sealed class BossPhaseAbilityEntryAsset
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private BossAbilityAsset ability;
        [Min(0f)] [SerializeField] private float weightMultiplier = 1f;
        [Tooltip("0 means unlimited uses in this phase.")]
        [Min(0)] [SerializeField] private int maxUses;
        [Min(0f)] [SerializeField] private float initialCooldown;

        public bool Enabled => enabled;
        public BossAbilityAsset Ability => ability;
        public float WeightMultiplier => weightMultiplier;
        public int MaxUses => maxUses;
        public float InitialCooldown => initialCooldown;
    }

    [CreateAssetMenu(fileName = "BossPhase", menuName = "Vampire Hunt/Boss/Phase")]
    public sealed class BossPhaseAsset : ScriptableObject
    {
        [Min(1)] [SerializeField] private int phaseNumber = 1;
        [SerializeField] private string displayName = "Phase 1";
        [SerializeField] private BossPhaseAbilityEntryAsset[] abilities =
            Array.Empty<BossPhaseAbilityEntryAsset>();

        public int PhaseNumber => phaseNumber;
        public IReadOnlyList<BossPhaseAbilityEntryAsset> Abilities => abilities;

        public BossPhaseDefinition CreateDefinition()
        {
            var entries = new List<BossPhaseAbilityEntry>();
            var seenIds = new HashSet<uint>();

            for (int i = 0; i < abilities.Length; i++)
            {
                BossPhaseAbilityEntryAsset entry = abilities[i];
                if (entry == null || !entry.Enabled || entry.Ability == null ||
                    !seenIds.Add(entry.Ability.AbilityId)) continue;
                entries.Add(new BossPhaseAbilityEntry(
                    entry.Ability.CreateDefinition(),
                    entry.WeightMultiplier,
                    entry.MaxUses,
                    entry.InitialCooldown));
            }

            return new BossPhaseDefinition(phaseNumber, displayName, entries.ToArray());
        }

        private void OnValidate()
        {
            phaseNumber = Mathf.Max(1, phaseNumber);
            abilities ??= Array.Empty<BossPhaseAbilityEntryAsset>();
        }
    }

    [CreateAssetMenu(fileName = "BossPhaseSet", menuName = "Vampire Hunt/Boss/Phase Set")]
    public sealed class BossPhaseSetAsset : ScriptableObject
    {
        [SerializeField] private BossPhaseAsset[] phases = Array.Empty<BossPhaseAsset>();

        public IReadOnlyList<BossPhaseAsset> Phases => phases;

        public bool TryCreateDefinition(int phaseNumber, out BossPhaseDefinition definition)
        {
            definition = null;
            for (int i = 0; i < phases.Length; i++)
            {
                BossPhaseAsset phase = phases[i];
                if (phase == null || phase.PhaseNumber != phaseNumber) continue;
                definition = phase.CreateDefinition();
                return true;
            }
            return false;
        }

        public bool TryGetAbility(uint abilityId, out BossAbilityAsset ability)
        {
            for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                BossPhaseAsset phase = phases[phaseIndex];
                if (phase == null) continue;
                IReadOnlyList<BossPhaseAbilityEntryAsset> entries = phase.Abilities;
                for (int abilityIndex = 0; abilityIndex < entries.Count; abilityIndex++)
                {
                    BossAbilityAsset candidate = entries[abilityIndex]?.Ability;
                    if (candidate == null || candidate.AbilityId != abilityId) continue;
                    ability = candidate;
                    return true;
                }
            }

            ability = null;
            return false;
        }

        private void OnValidate()
        {
            phases ??= Array.Empty<BossPhaseAsset>();
        }
    }
}
