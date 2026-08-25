using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Boss.Abilities;

namespace VampireHunt.Infrastructure.Unity.Boss
{
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

        [Header("Gameplay Parameters")]
        [SerializeField] private BossAbilityTuning tuning = new BossAbilityTuning();

        [Header("Client Presentation Timeline")]
        [SerializeField] private BossAbilityPresentationCue[] presentationCues =
            Array.Empty<BossAbilityPresentationCue>();

        public uint AbilityId => abilityId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public UnityEngine.Object LogicScript => logicScript;
        public string LogicTypeName => logicTypeName;
        public BossAbilityTuning Tuning => tuning;
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
                BossAbilityLogicTypeResolver.CreateFactory(logicTypeName, tuning));
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
            tuning ??= new BossAbilityTuning();
        }
    }
}
