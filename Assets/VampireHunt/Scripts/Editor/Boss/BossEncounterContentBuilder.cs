using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VampireHunt.Boss.Abilities;
using VampireHunt.Boss.Abilities.Logic;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Infrastructure.Unity.Boss;
using VampireHunt.Navigation;
using VampireHunt.Presentation.Boss;
using VampireHunt.Presentation.HUD;
using Object = UnityEngine.Object;

namespace VampireHunt.Editor.Boss
{
    public static class BossEncounterContentBuilder
    {
        private const string BossDataRoot = "Assets/VampireHunt/Data/Boss";
        private const string EncounterDataRoot = BossDataRoot + "/Encounter";
        private const string SkillRoot = BossDataRoot + "/Skills/Formal";
        private const string PhaseRoot = BossDataRoot + "/Phases/Formal";
        private const string PrefabRoot = "Assets/VampireHunt/Prefabs/Boss";
        private const string VfxRoot = PrefabRoot + "/BossVFX/Formal";
        private const string PresentationRoot = "Assets/VampireHunt/Presentation/Boss";
        private const string MaterialRoot = PresentationRoot + "/Materials";
        private const string AtlasPath = PresentationRoot + "/Textures/VH_Boss_BloodMagic_VFXAtlas.png";
        private const string ChargeSlashTexturePath = PresentationRoot + "/Textures/VH_Boss_ChargeSlash_SwordQi.png";
        private const string ChargeSlashWarningShaderPath = PresentationRoot + "/Shaders/VH_Boss_ChargeSlashTelegraph.shader";
        private const string ChargeSlashDissolveShaderPath = PresentationRoot + "/Shaders/VH_Boss_SlashDissolveParticle.shader";
        private const string ChargeSlashWarningPrefabPath = VfxRoot + "/VH_BossSkill_ChargeSlash_WarningBox_VFX.prefab";
        private const string ChargeSlashSwordQiPrefabPath = VfxRoot + "/VH_BossSkill_ChargeSlash_SwordQiProjectile_VFX.prefab";
        private const string SweepTexturePath = PresentationRoot + "/Textures/VH_Boss_Sweep_BloodArc.png";
        private const string SweepWarningShaderPath = PresentationRoot + "/Shaders/VH_Boss_SweepWaveTelegraph.shader";
        private const string SweepRevealShaderPath = PresentationRoot + "/Shaders/VH_Boss_SweepWaveRevealParticle.shader";
        private const string SweepWarningPrefabPath = VfxRoot + "/VH_BossSkill_Sweep_WaveWarning_VFX.prefab";
        private const string SweepAttackPrefabPath = VfxRoot + "/VH_BossSkill_Sweep_BloodArcReveal_VFX.prefab";
        private const string LaserMarkerShaderPath = PresentationRoot + "/Shaders/VH_Boss_TrackingLaserTargetMarker.shader";
        private const string LaserBeamShaderPath = PresentationRoot + "/Shaders/VH_Boss_TrackingLaserBeam.shader";
        private const string LaserMarkerPrefabPath = VfxRoot + "/VH_BossSkill_Laser_TargetMarker_VFX.prefab";
        private const string LaserBeamPrefabPath = VfxRoot + "/VH_BossSkill_Laser_TrackingBeam_VFX.prefab";
        private const string BossPrefabPath = PrefabRoot + "/VH_Boss.prefab";
        private const string HandPrefabPath = PrefabRoot + "/VH_BossHand.prefab";
        private const string ProjectilePrefabPath = PrefabRoot + "/VH_Boss_BloodProjectile.prefab";
        private const string ConfigPath = EncounterDataRoot + "/VH_Boss_Encounter.asset";
        private const string PhaseSetPath = PhaseRoot + "/VH_Boss_PhaseSet.asset";
        private const string NetworkPrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        // SampleScene is the project's current playable scene. It still contains the old
        // scene-authored Boss_BloodLord, so the builder migrates that object in-place to
        // the formal prefab instead of silently placing the encounter in an unused scene.
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string PactPath = "Assets/VampireHunt/Data/Pacts/RagnarokFinale.asset";
        private const string PactCatalogPath = "Assets/VampireHunt/Data/Pacts/PactCatalog.asset";

        private readonly struct AbilitySpec
        {
            public readonly string FileName;
            public readonly string DisplayName;
            public readonly uint Id;
            public readonly Type LogicType;
            public readonly int AtlasCell;
            public readonly float Cooldown;
            public readonly float Telegraph;
            public readonly float Resolve;
            public readonly float Recover;
            public readonly bool RequiresTarget;
            public readonly bool OneShot;
            public readonly float MinHealth;
            public readonly float MaxHealth;
            public readonly bool EachLockedArea;
            public readonly Action<SerializedProperty> ConfigureTuning;

            public AbilitySpec(string fileName, string displayName, uint id, Type logicType,
                int atlasCell, float cooldown, float telegraph, float resolve, float recover,
                bool requiresTarget, bool oneShot, float minHealth, float maxHealth,
                Action<SerializedProperty> configureTuning, bool eachLockedArea = false)
            {
                FileName = fileName; DisplayName = displayName; Id = id; LogicType = logicType;
                AtlasCell = atlasCell; Cooldown = cooldown; Telegraph = telegraph; Resolve = resolve;
                Recover = recover; RequiresTarget = requiresTarget; OneShot = oneShot;
                MinHealth = minHealth; MaxHealth = maxHealth; ConfigureTuning = configureTuning;
                EachLockedArea = eachLockedArea;
            }
        }

        private readonly struct PhaseEntry
        {
            public readonly BossAbilityAsset Ability;
            public readonly float Weight;
            public readonly int MaxUses;
            public readonly float InitialCooldown;
            public PhaseEntry(BossAbilityAsset ability, float weight, int maxUses = 0, float initialCooldown = 0f)
            { Ability = ability; Weight = weight; MaxUses = maxUses; InitialCooldown = initialCooldown; }
        }

        [MenuItem("Tools/Vampire Hunt/Boss/Build Complete Boss Encounter", priority = 190)]
        public static void Build()
        {
            EnsureFolder(EncounterDataRoot);
            EnsureFolder(SkillRoot);
            EnsureFolder(PhaseRoot);
            EnsureFolder(VfxRoot);
            EnsureFolder(MaterialRoot);

            ConfigureAtlasImporter();
            var vfxByCell = new Dictionary<int, GameObject>();
            for (int cell = 0; cell < 9; cell++)
                vfxByCell[cell] = CreateOrUpdateVfx(cell, CellName(cell));
            GameObject chargeSlashWarning = CreateOrUpdateChargeSlashWarningVfx();
            GameObject chargeSlashSwordQi = CreateOrUpdateChargeSlashSwordQiVfx();
            GameObject sweepWarning = CreateOrUpdateSweepWarningVfx();
            GameObject sweepAttack = CreateOrUpdateSweepAttackVfx();
            GameObject laserMarker = CreateOrUpdateLaserTargetMarkerVfx();
            GameObject laserBeam = CreateOrUpdateLaserBeamVfx();

            var abilities = new Dictionary<uint, BossAbilityAsset>();
            foreach (AbilitySpec spec in CreateAbilitySpecs())
                abilities[spec.Id] = CreateOrUpdateAbility(
                    spec,
                    vfxByCell[spec.AtlasCell],
                    chargeSlashWarning,
                    chargeSlashSwordQi,
                    sweepWarning,
                    sweepAttack,
                    laserMarker,
                    laserBeam);

            BossPhaseAsset phase1 = CreateOrUpdatePhase(1, "第一阶段",
                new PhaseEntry(abilities[2010], 1f, initialCooldown: 0.4f),
                new PhaseEntry(abilities[2020], 0.8f),
                new PhaseEntry(abilities[2040], 1f),
                new PhaseEntry(abilities[2030], 0.8f));
            BossPhaseAsset phase2 = CreateOrUpdatePhase(2, "第二阶段",
                new PhaseEntry(abilities[2010], 1f, initialCooldown: 0.4f),
                new PhaseEntry(abilities[2020], 0.8f),
                new PhaseEntry(abilities[2040], 1f),
                new PhaseEntry(abilities[2030], 0.8f),
                new PhaseEntry(abilities[2050], 1f, initialCooldown: 0.4f));
            BossPhaseAsset phase3 = CreateOrUpdatePhase(3, "第三阶段",
                new PhaseEntry(abilities[2010], 1f, initialCooldown: 0.4f),
                new PhaseEntry(abilities[2020], 0.8f),
                new PhaseEntry(abilities[2040], 1f),
                new PhaseEntry(abilities[2030], 0.8f),
                new PhaseEntry(abilities[2050], 1f, initialCooldown: 0.4f),
                new PhaseEntry(abilities[2070], 1f, initialCooldown: 0.35f),
                new PhaseEntry(abilities[2060], 0f, maxUses: 1));
            BossPhaseAsset roaming = CreateOrUpdatePhase(4, "游走状态招式库",
                new PhaseEntry(abilities[2120], 1f, initialCooldown: 1f),
                new PhaseEntry(abilities[2130], 1f, initialCooldown: 1.5f),
                new PhaseEntry(abilities[2001], 0f),
                new PhaseEntry(abilities[2002], 0f),
                new PhaseEntry(abilities[2003], 0f),
                new PhaseEntry(abilities[2090], 0f));
            BossPhaseSetAsset phaseSet = CreateOrUpdatePhaseSet(phase1, phase2, phase3, roaming);
            BossEncounterConfigAsset config = CreateOrUpdateEncounterConfig();
            CreateOrUpdateFrenzyPact();

            GameObject projectile = CreateOrUpdateProjectile(vfxByCell[1]);
            GameObject hand = CreateOrUpdateHand();
            GameObject boss = CreateOrUpdateBoss(config, phaseSet, projectile, hand);
            RegisterNetworkPrefab(boss);
            RegisterNetworkPrefab(projectile);
            RegisterNetworkPrefab(hand);
            PlaceBossInScene(boss);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = boss;
            EditorGUIUtility.PingObject(boss);
            Debug.Log("[BossEncounterContentBuilder] Complete server-authoritative Boss encounter built and placed.", boss);
        }

        private static AbilitySpec[] CreateAbilitySpecs()
        {
            Action<SerializedProperty> sweep = p =>
            {
                SetTuning(p, 24, 7, 0, 8, 5, 2, 2, .45f, 1, 1, 1, 1, 0, 1, 0, 2);
                p.FindPropertyRelative("TravelDuration").floatValue = .28f;
                p.FindPropertyRelative("DissolveDuration").floatValue = .2f;
                p.FindPropertyRelative("VfxHeight").floatValue = 1.4f;
            };
            Action<SerializedProperty> volley = p => SetTuning(p, 12, 0, 0, 8, 1, 1, 1, .12f, 1, 28, 7, .7f, 45, 2, 0, 2);
            Action<SerializedProperty> bomb = p => SetTuning(p, 32, 10, 3.2f, 100, 2, 1, 1, .2f, 1, 1, 1, 1, 0, 1, 0, 2);
            Action<SerializedProperty> slash = p =>
            {
                SetTuning(p, 38, 9, 0, 10, 3, 2, 1, .2f, 1, 1, 1, 1, 0, 1, 0, 2);
                p.FindPropertyRelative("TravelDuration").floatValue = .45f;
                p.FindPropertyRelative("DissolveDuration").floatValue = .35f;
                p.FindPropertyRelative("VfxHeight").floatValue = 1.2f;
            };
            Action<SerializedProperty> grid = p => SetTuning(p, 26, 0, 0, 14, .65f, 2, 3, .42f, 1, 1, 1, 1, 0, 1.5f, 0, 2);
            Action<SerializedProperty> laser = p => SetTuning(p, 17, 6, 0, 16, 1.2f, 2, 1, .4f, 1, 1, 1, 1, 30, 3, 0, 2);
            Action<SerializedProperty> shock = p => SetTuning(p, 20, 8, 13, 1, 1, 2, 5, .22f, 1, 1, 1, 1, 0, 1.1f, 0, 2);
            Action<SerializedProperty> frenzy = p => SetTuning(p, 0, 0, 0, 1, 1, 1, 1, .2f, 1, 1, 1, 1, 0, 1, 0, 2);

            return new[]
            {
                new AbilitySpec("VH_Boss_StaggerFullScreenBarrage", "踉跄·全屏弹幕", 2001, typeof(BossRadialVolleyAbilityLogic), 1, 0, .5f, 2f, .5f, false, false, 0, 1,
                    p => SetTuning(p, 10, 0, 0, 8, 1, 1, 1, .08f, 1, 40, 6, .65f, 65, 2, 0, 2)),
                new AbilitySpec("VH_Boss_StaggerStepShockwave", "踉跄·步进震波", 2002, typeof(BossRadialShockwaveAbilityLogic), 6, 0, .8f, 1.1f, .5f, false, false, 0, 1, shock),
                new AbilitySpec("VH_Boss_StaggerTrackingLaser", "踉跄·跟踪激光", 2003, typeof(BossLaserSweepAbilityLogic), 5, 0, .7f, 1.5f, .3f, true, false, 0, 1, laser),
                new AbilitySpec("VH_Boss_Sweep", "横扫", 2010, typeof(BossSweepAbilityLogic), 0, 1.5f, .45f, .65f, .7f, true, false, 0, 1, sweep),
                new AbilitySpec("VH_Boss_RadialVolley", "弹幕", 2020, typeof(BossRadialVolleyAbilityLogic), 1, 2.5f, .8f, 2f, .8f, false, false, 0, 1, volley),
                new AbilitySpec("VH_Boss_Bombardment", "轰炸", 2030, typeof(BossBombardmentAbilityLogic), 2, 2.5f, 1.2f, .12f, .6f, true, false, 0, 1, bomb, eachLockedArea: true),
                new AbilitySpec("VH_Boss_ChargeSlash", "斩击", 2040, typeof(BossChargeSlashAbilityLogic), 3, 2.25f, 1f, .12f, .7f, true, false, 0, 1, slash),
                new AbilitySpec("VH_Boss_GridCut", "网格", 2050, typeof(BossGridCutAbilityLogic), 4, 3f, 1.1f, 1.5f, .7f, false, false, 0, 1, grid),
                new AbilitySpec("VH_Boss_Frenzy", "狂暴", 2060, typeof(BossFrenzyAbilityLogic), 7, 0, 1.5f, .1f, .6f, false, true, 0, .2f, frenzy),
                new AbilitySpec("VH_Boss_LaserSweep", "激光", 2070, typeof(BossLaserSweepAbilityLogic), 5, 4f, .8f, 3f, .8f, true, false, 0, 1, laser),
                new AbilitySpec("VH_Boss_PhaseTransitionAura", "阶段转换·黄金血辉", 2090, typeof(BossPhaseAuraAbilityLogic), 8, 0, 0, 1.2f, .4f, false, false, 0, 1, frenzy),
                new AbilitySpec("VH_Boss_RoamingRadialVolley", "游走·弹幕", 2120, typeof(BossRadialVolleyAbilityLogic), 1, 4f, .8f, 2f, .8f, true, false, 0, 1, volley),
                new AbilitySpec("VH_Boss_RoamingBombardment", "游走·轰炸", 2130, typeof(BossBombardmentAbilityLogic), 2, 4f, 1.2f, .12f, .6f, true, false, 0, 1, bomb, eachLockedArea: true)
            };
        }

        private static BossAbilityAsset CreateOrUpdateAbility(
            AbilitySpec spec,
            GameObject vfx,
            GameObject chargeSlashWarning,
            GameObject chargeSlashSwordQi,
            GameObject sweepWarning,
            GameObject sweepAttack,
            GameObject laserMarker,
            GameObject laserBeam)
        {
            string path = $"{SkillRoot}/{spec.FileName}.asset";
            BossAbilityAsset asset = GetOrCreateAsset<BossAbilityAsset>(path);
            var so = new SerializedObject(asset);
            so.FindProperty("abilityId").uintValue = spec.Id;
            so.FindProperty("displayName").stringValue = spec.DisplayName;
            so.FindProperty("description").stringValue = "服务器权威技能。数据、逻辑和表现引用均可独立替换。";
            so.FindProperty("baseWeight").floatValue = 1f;
            so.FindProperty("cooldown").floatValue = spec.Cooldown;
            so.FindProperty("minDistance").floatValue = 0f;
            so.FindProperty("maxDistance").floatValue = spec.Id == 2010 ? 8f : spec.Id == 2040 ? 10f : 100f;
            so.FindProperty("minNormalizedHealth").floatValue = spec.MinHealth;
            so.FindProperty("maxNormalizedHealth").floatValue = spec.MaxHealth;
            so.FindProperty("requiresTarget").boolValue = spec.RequiresTarget;
            so.FindProperty("oneShot").boolValue = spec.OneShot;
            so.FindProperty("parryableDuringTelegraph").boolValue = spec.Id == 2040;
            so.FindProperty("telegraphDuration").floatValue = spec.Telegraph;
            so.FindProperty("resolveDuration").floatValue = spec.Resolve;
            so.FindProperty("recoverDuration").floatValue = spec.Recover;
            MonoScript logic = FindScriptForType(spec.LogicType);
            so.FindProperty("logicScript").objectReferenceValue = logic;
            so.FindProperty("logicTypeName").stringValue = spec.LogicType.AssemblyQualifiedName;
            spec.ConfigureTuning?.Invoke(so.FindProperty("tuning"));
            SerializedProperty cues = so.FindProperty("presentationCues");
            cues.arraySize = 2;
            if (spec.Id == 2010)
            {
                ConfigureCue(cues.GetArrayElementAtIndex(0), $"{spec.DisplayName}·方向波浪预警", 0f,
                    BossAbilityAnchorId.Ground, sweepWarning, Vector3.one,
                    spec.Telegraph + .02f, BossAbilityCueSpawnMode.SweepWaveWarning);
                ConfigureCue(cues.GetArrayElementAtIndex(1), $"{spec.DisplayName}·血刃横向显现", spec.Telegraph,
                    BossAbilityAnchorId.Ground, sweepAttack, Vector3.one,
                    .42f, BossAbilityCueSpawnMode.SweepWaveAttack);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                return asset;
            }
            if (spec.Id == 2040)
            {
                ConfigureCue(cues.GetArrayElementAtIndex(0), $"{spec.DisplayName}·固定矩形预警", 0f,
                    BossAbilityAnchorId.Ground, chargeSlashWarning, Vector3.one,
                    spec.Telegraph + spec.Resolve, BossAbilityCueSpawnMode.EachLockedArea);
                ConfigureCue(cues.GetArrayElementAtIndex(1), $"{spec.DisplayName}·移动剑气与终点消解", spec.Telegraph,
                    BossAbilityAnchorId.Ground, chargeSlashSwordQi, Vector3.one * 1.25f,
                    .8f, BossAbilityCueSpawnMode.DirectionalTravel);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                return asset;
            }
            if (spec.LogicType == typeof(BossLaserSweepAbilityLogic))
            {
                SerializedProperty markerCue = cues.GetArrayElementAtIndex(0);
                ConfigureCue(markerCue, $"{spec.DisplayName}·锁定玩家三角", 0f,
                    BossAbilityAnchorId.Head, laserMarker, Vector3.one,
                    spec.Telegraph + spec.Resolve, BossAbilityCueSpawnMode.TrackingLaserTargetMarker);
                markerCue.FindPropertyRelative("localPosition").vector3Value = Vector3.up * 2.5f;
                markerCue.FindPropertyRelative("followAnchor").boolValue = false;

                SerializedProperty beamCue = cues.GetArrayElementAtIndex(1);
                ConfigureCue(beamCue, $"{spec.DisplayName}·追踪长方体激光", spec.Telegraph,
                    BossAbilityAnchorId.Chest, laserBeam, Vector3.one,
                    spec.Resolve, BossAbilityCueSpawnMode.TrackingLaserBeam);
                beamCue.FindPropertyRelative("localPosition").vector3Value = Vector3.up;
                beamCue.FindPropertyRelative("followAnchor").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                return asset;
            }
            BossAbilityCueSpawnMode spawnMode = spec.EachLockedArea
                ? BossAbilityCueSpawnMode.EachLockedArea
                : BossAbilityCueSpawnMode.SingleAnchor;
            ConfigureCue(cues.GetArrayElementAtIndex(0), $"{spec.DisplayName}·预警", 0f,
                spec.AtlasCell == 0 ? BossAbilityAnchorId.Chest : BossAbilityAnchorId.Ground,
                vfx, Vector3.one * (spec.EachLockedArea ? 1f : .7f),
                spec.EachLockedArea ? 0f : spec.Telegraph + .2f, spawnMode);
            ConfigureCue(cues.GetArrayElementAtIndex(1), $"{spec.DisplayName}·释放", spec.Telegraph,
                BossAbilityAnchorId.Ground, vfx, Vector3.one * 1.25f,
                spec.Resolve + .35f, spawnMode);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void SetTuning(SerializedProperty p, float damage, float knockback, float radius,
            float range, float width, float height, int repetitions, float interval, uint projectileId,
            int projectileCount, float projectileSpeed, float projectileScale, float rotationSpeed,
            float duration, uint statusId, float clockDrainRate)
        {
            p.FindPropertyRelative("Damage").floatValue = damage;
            p.FindPropertyRelative("Knockback").floatValue = knockback;
            p.FindPropertyRelative("Radius").floatValue = radius;
            p.FindPropertyRelative("Range").floatValue = range;
            p.FindPropertyRelative("Width").floatValue = width;
            p.FindPropertyRelative("Height").floatValue = height;
            p.FindPropertyRelative("Repetitions").intValue = repetitions;
            p.FindPropertyRelative("Interval").floatValue = interval;
            p.FindPropertyRelative("ProjectileId").uintValue = projectileId;
            p.FindPropertyRelative("ProjectileCount").intValue = projectileCount;
            p.FindPropertyRelative("ProjectileSpeed").floatValue = projectileSpeed;
            p.FindPropertyRelative("ProjectileScale").floatValue = projectileScale;
            p.FindPropertyRelative("RotationSpeed").floatValue = rotationSpeed;
            p.FindPropertyRelative("Duration").floatValue = duration;
            p.FindPropertyRelative("StatusId").uintValue = statusId;
            p.FindPropertyRelative("ClockDrainRate").floatValue = clockDrainRate;
            p.FindPropertyRelative("TravelDuration").floatValue = .45f;
            p.FindPropertyRelative("DissolveDuration").floatValue = .3f;
            p.FindPropertyRelative("VfxHeight").floatValue = 1.2f;
        }

        private static BossPhaseAsset CreateOrUpdatePhase(int number, string name, params PhaseEntry[] rows)
        {
            BossPhaseAsset asset = GetOrCreateAsset<BossPhaseAsset>($"{PhaseRoot}/VH_Boss_Phase{number}.asset");
            var so = new SerializedObject(asset);
            so.FindProperty("phaseNumber").intValue = number;
            so.FindProperty("displayName").stringValue = name;
            SerializedProperty entries = so.FindProperty("abilities");
            entries.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("enabled").boolValue = true;
                entry.FindPropertyRelative("ability").objectReferenceValue = rows[i].Ability;
                entry.FindPropertyRelative("weightMultiplier").floatValue = rows[i].Weight;
                entry.FindPropertyRelative("maxUses").intValue = rows[i].MaxUses;
                entry.FindPropertyRelative("initialCooldown").floatValue = rows[i].InitialCooldown;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static BossPhaseSetAsset CreateOrUpdatePhaseSet(params BossPhaseAsset[] phases)
        {
            BossPhaseSetAsset asset = GetOrCreateAsset<BossPhaseSetAsset>(PhaseSetPath);
            var so = new SerializedObject(asset);
            SerializedProperty rows = so.FindProperty("phases");
            rows.arraySize = phases.Length;
            for (int i = 0; i < phases.Length; i++) rows.GetArrayElementAtIndex(i).objectReferenceValue = phases[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static BossEncounterConfigAsset CreateOrUpdateEncounterConfig()
        {
            BossEncounterConfigAsset asset = GetOrCreateAsset<BossEncounterConfigAsset>(ConfigPath);
            var so = new SerializedObject(asset);
            so.FindProperty("bossName").stringValue = "猩红之主";
            so.FindProperty("roamingAbilityPhaseNumber").intValue = 4;
            so.FindProperty("phaseAuraAbilityId").uintValue = 2090;
            so.FindProperty("frenzyAbilityId").uintValue = 2060;
            so.FindProperty("requiredFrenzyPactId").uintValue = 200;
            so.FindProperty("staggerEffectDuration").floatValue = 3.2f;
            SerializedProperty stages = so.FindProperty("stages");
            stages.arraySize = 3;
            float[] guards = { 100f, 180f, 280f };
            float[] health = { 300f, 450f, 650f };
            for (int i = 0; i < 3; i++)
            {
                SerializedProperty row = stages.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("GuardHealth").floatValue = guards[i];
                row.FindPropertyRelative("BattleHealth").floatValue = health[i];
                row.FindPropertyRelative("AbilityPhaseNumber").intValue = i + 1;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void CreateOrUpdateFrenzyPact()
        {
            PactDefinitionAsset pact = GetOrCreateAsset<PactDefinitionAsset>(PactPath);
            var so = new SerializedObject(pact);
            so.FindProperty("pactId").uintValue = 200;
            so.FindProperty("displayName").stringValue = "诸神黄昏·末曲";
            so.FindProperty("description").stringValue = "副契：允许第三阶段 Boss 释放狂暴，契约倒计时改为每秒消耗 2 秒。";
            so.FindProperty("repeatable").boolValue = false;
            so.FindProperty("maxStacks").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pact);

            PactCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<PactCatalogAsset>(PactCatalogPath);
            if (catalog == null) return;
            var catalogSo = new SerializedObject(catalog);
            SerializedProperty definitions = catalogSo.FindProperty("definitions");
            for (int i = 0; i < definitions.arraySize; i++)
                if (definitions.GetArrayElementAtIndex(i).objectReferenceValue == pact) return;
            definitions.InsertArrayElementAtIndex(definitions.arraySize);
            definitions.GetArrayElementAtIndex(definitions.arraySize - 1).objectReferenceValue = pact;
            catalogSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static GameObject CreateOrUpdateVfx(int cell, string skillName)
        {
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            Shader shader = Shader.Find("VampireHunt/Boss/AdditiveVFX") ?? Shader.Find("Universal Render Pipeline/Unlit");
            string materialPath = $"{MaterialRoot}/VH_BossSkill_{skillName}_VFX.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader) { name = $"VH_BossSkill_{skillName}_VFX" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else material.shader = shader;
            material.SetTexture("_BaseMap", atlas);
            material.SetTextureScale("_BaseMap", Vector2.one / 3f);
            int column = cell % 3;
            int row = 2 - cell / 3;
            material.SetTextureOffset("_BaseMap", new Vector2(column / 3f, row / 3f));
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", cell == 8 ? new Color(1f, .72f, .18f, 1f) : Color.white);
            if (material.HasProperty("_Intensity")) material.SetFloat("_Intensity", cell == 8 ? 6f : 4f);
            EditorUtility.SetDirty(material);

            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Quad);
            root.name = $"VH_BossSkill_{skillName}_VFX";
            try
            {
                Object.DestroyImmediate(root.GetComponent<Collider>());
                root.GetComponent<Renderer>().sharedMaterial = material;
                BossVfxPulsePresenter pulse = root.AddComponent<BossVfxPulsePresenter>();
                SetFloat(pulse, "rotationDegreesPerSecond", cell == 5 ? 0f : 28f + cell * 4f);
                SetFloat(pulse, "pulseAmount", .09f);
                SetBoolean(pulse, "faceGround", true);

                GameObject sparkObject = new GameObject("BloodSparks");
                sparkObject.transform.SetParent(root.transform, false);
                ParticleSystem particles = sparkObject.AddComponent<ParticleSystem>();
                var main = particles.main;
                main.duration = 1f; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 2.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(.03f, .14f);
                main.startColor = cell == 8 ? new Color(1f, .78f, .25f, 1f) : new Color(1f, .02f, .08f, 1f);
                var emission = particles.emission; emission.rateOverTime = 35f;
                var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .45f;
                ParticleSystemRenderer psRenderer = sparkObject.GetComponent<ParticleSystemRenderer>();
                psRenderer.sharedMaterial = material;

                GameObject lightObject = new GameObject("SkillLight");
                lightObject.transform.SetParent(root.transform, false);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = cell == 8 ? new Color(1f, .7f, .2f) : new Color(1f, 0f, .05f);
                light.intensity = 4f; light.range = 5f;
                return PrefabUtility.SaveAsPrefabAsset(root, $"{VfxRoot}/VH_BossSkill_{skillName}_VFX.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateChargeSlashWarningVfx()
        {
            GameObject root = new GameObject("VH_BossSkill_ChargeSlash_WarningBox_VFX");
            try
            {
                BossChargeSlashWarningVfxPresenter presenter =
                    root.AddComponent<BossChargeSlashWarningVfxPresenter>();
                SetObject(presenter, "warningShader",
                    AssetDatabase.LoadAssetAtPath<Shader>(ChargeSlashWarningShaderPath));
                return PrefabUtility.SaveAsPrefabAsset(root, ChargeSlashWarningPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateChargeSlashSwordQiVfx()
        {
            GameObject root = new GameObject("VH_BossSkill_ChargeSlash_SwordQiProjectile_VFX");
            try
            {
                BossChargeSlashSwordQiVfxPresenter presenter =
                    root.AddComponent<BossChargeSlashSwordQiVfxPresenter>();
                SetObject(presenter, "dissolveShader",
                    AssetDatabase.LoadAssetAtPath<Shader>(ChargeSlashDissolveShaderPath));
                SetObject(presenter, "swordQiTexture",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(ChargeSlashTexturePath));
                return PrefabUtility.SaveAsPrefabAsset(root, ChargeSlashSwordQiPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateSweepWarningVfx()
        {
            GameObject root = new GameObject("VH_BossSkill_Sweep_WaveWarning_VFX");
            try
            {
                BossSweepWaveWarningVfxPresenter presenter =
                    root.AddComponent<BossSweepWaveWarningVfxPresenter>();
                SetObject(presenter, "warningShader",
                    AssetDatabase.LoadAssetAtPath<Shader>(SweepWarningShaderPath));
                return PrefabUtility.SaveAsPrefabAsset(root, SweepWarningPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateSweepAttackVfx()
        {
            GameObject root = new GameObject("VH_BossSkill_Sweep_BloodArcReveal_VFX");
            try
            {
                BossSweepWaveAttackVfxPresenter presenter =
                    root.AddComponent<BossSweepWaveAttackVfxPresenter>();
                SetObject(presenter, "revealShader",
                    AssetDatabase.LoadAssetAtPath<Shader>(SweepRevealShaderPath));
                SetObject(presenter, "sweepTexture",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(SweepTexturePath));
                return PrefabUtility.SaveAsPrefabAsset(root, SweepAttackPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateLaserTargetMarkerVfx()
        {
            GameObject root = new GameObject("VH_BossSkill_Laser_TargetMarker_VFX");
            try
            {
                BossTrackingLaserTargetMarkerVfxPresenter presenter =
                    root.AddComponent<BossTrackingLaserTargetMarkerVfxPresenter>();
                SetObject(presenter, "markerShader",
                    AssetDatabase.LoadAssetAtPath<Shader>(LaserMarkerShaderPath));
                return PrefabUtility.SaveAsPrefabAsset(root, LaserMarkerPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateLaserBeamVfx()
        {
            GameObject root = new GameObject("VH_BossSkill_Laser_TrackingBeam_VFX");
            try
            {
                BossTrackingLaserBeamVfxPresenter presenter =
                    root.AddComponent<BossTrackingLaserBeamVfxPresenter>();
                SetObject(presenter, "beamShader",
                    AssetDatabase.LoadAssetAtPath<Shader>(LaserBeamShaderPath));
                return PrefabUtility.SaveAsPrefabAsset(root, LaserBeamPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateProjectile(GameObject vfxPrefab)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "VH_Boss_BloodProjectile";
            try
            {
                root.transform.localScale = Vector3.one * .45f;
                root.layer = 0;
                SphereCollider collider = root.GetComponent<SphereCollider>();
                collider.isTrigger = true;
                GetOrAdd<NetworkObject>(root);
                GetOrAdd<NetworkTransform>(root);
                GetOrAdd<BossProjectileNetworkActor>(root);
                if (vfxPrefab != null)
                {
                    GameObject vfx = (GameObject)PrefabUtility.InstantiatePrefab(vfxPrefab);
                    vfx.name = "VH_BossSkill_RadialVolley_ProjectileVFX";
                    vfx.transform.SetParent(root.transform, false);
                    vfx.transform.localScale = Vector3.one * .9f;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, ProjectilePrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateHand()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
            if (source == null) throw new InvalidOperationException($"Boss prefab missing: {BossPrefabPath}");
            GameObject root = new GameObject("VH_BossHand");
            try
            {
                root.layer = 8;
                GetOrAdd<NetworkObject>(root);
                GetOrAdd<NetworkTransform>(root);
                GetOrAdd<BossHandNetworkActor>(root);
                CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
                collider.center = Vector3.up * .7f; collider.height = 1.4f; collider.radius = .45f;

                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                visual.name = "BossModel_HandVisual";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                StripToVisualOnly(visual);
                SetLayerRecursively(visual, 8);
                return PrefabUtility.SaveAsPrefabAsset(root, HandPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateOrUpdateBoss(BossEncounterConfigAsset config, BossPhaseSetAsset phaseSet,
            GameObject projectilePrefab, GameObject handPrefab)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BossPrefabPath);
            try
            {
                root.name = "Boss";
                root.layer = 8;
                GetOrAdd<NetworkObject>(root);
                GetOrAdd<NetworkTransform>(root);
                CharacterController controller = GetOrAdd<CharacterController>(root);

                Transform left = GetOrCreateAnchor(root.transform, "AbilityAnchor_LeftHand", new Vector3(-1.2f, 1.3f, 0));
                Transform right = GetOrCreateAnchor(root.transform, "AbilityAnchor_RightHand", new Vector3(1.2f, 1.3f, 0));
                GetOrCreateAnchor(root.transform, "AbilityAnchor_Chest", new Vector3(0, 1.4f, 0));
                GetOrCreateAnchor(root.transform, "AbilityAnchor_Head", new Vector3(0, 2.1f, 0));
                GetOrCreateAnchor(root.transform, "AbilityAnchor_Ground", Vector3.zero);

                BossAbilityPhaseProvider phaseProvider = GetOrAdd<BossAbilityPhaseProvider>(root);
                SetObject(phaseProvider, "phaseSet", phaseSet); SetInteger(phaseProvider, "startingPhase", 4);
                BossAbilityContextProvider context = GetOrAdd<BossAbilityContextProvider>(root);
                SetBoolean(context, "useForwardPreviewWhenTargetIsMissing", false);
                GetOrAdd<BossPlayerTargetQuery>(root);
                GetOrAdd<BossPhysicsHitQuery>(root);
                GetOrAdd<BossDamageService>(root);
                BossProjectileSpawner spawner = GetOrAdd<BossProjectileSpawner>(root);
                ConfigureProjectileCatalog(spawner, projectilePrefab);
                GetOrAdd<BossStatusEffectService>(root);
                GetOrAdd<BossRunClockModifier>(root);
                BossAreaTelegraphNetworkBridge areaTelegraph = GetOrAdd<BossAreaTelegraphNetworkBridge>(root);
                BossSweepTelegraphNetworkBridge sweepTelegraph = GetOrAdd<BossSweepTelegraphNetworkBridge>(root);
                BossTrackingLaserNetworkBridge laserPresentation = GetOrAdd<BossTrackingLaserNetworkBridge>(root);
                GetOrAdd<BossFacingService>(root);
                BossBodyStateHost body = GetOrAdd<BossBodyStateHost>(root);
                BossAbilityServiceHost serviceHost = GetOrAdd<BossAbilityServiceHost>(root);
                BossAbilityHost host = GetOrAdd<BossAbilityHost>(root); SetObject(host, "serviceHost", serviceHost);
                BossAbilityStateReplicator abilityReplicator = GetOrAdd<BossAbilityStateReplicator>(root);
                BossAbilityServerDriver driver = GetOrAdd<BossAbilityServerDriver>(root);
                SetObject(driver, "host", host); SetObject(driver, "phaseProvider", phaseProvider);
                SetObject(driver, "contextProvider", context); SetObject(driver, "stateReplicator", abilityReplicator);
                SetBoolean(driver, "allowAutomaticCasts", false); SetBoolean(driver, "offlinePreview", false);

                BossRoamingMovement movement = GetOrAdd<BossRoamingMovement>(root);
                SetObject(movement, "config", config); SetObject(movement, "characterController", controller);
                BossEncounterStateReplicator encounterReplicator = GetOrAdd<BossEncounterStateReplicator>(root);
                BossEncounterDirector director = GetOrAdd<BossEncounterDirector>(root);
                SetObject(director, "config", config); SetObject(director, "stateReplicator", encounterReplicator);
                SetObject(director, "abilityDriver", driver); SetObject(director, "roamingMovement", movement);
                SetObject(director, "bodyState", body);
                BossVitalsReceiver vitals = GetOrAdd<BossVitalsReceiver>(root);
                SetObject(vitals, "director", director); SetObject(vitals, "bodyState", body);
                SetObject(vitals, "abilityDriver", driver);
                BossHandCoordinator hands = GetOrAdd<BossHandCoordinator>(root);
                SetObject(hands, "handPrefab", handPrefab.GetComponent<NetworkObject>());
                SetObject(hands, "leftAnchor", left); SetObject(hands, "rightAnchor", right); SetObject(hands, "bodyState", body);
                BossHudBinder hud = GetOrAdd<BossHudBinder>(root);
                SetObject(hud, "config", config); SetObject(hud, "stateSource", encounterReplicator);

                BossAbilityAnchorRegistry anchors = GetOrAdd<BossAbilityAnchorRegistry>(root);
                anchors.EditorSetBindings(new[]
                {
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.Chest, root.transform.Find("AbilityAnchor_Chest")),
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.Head, root.transform.Find("AbilityAnchor_Head")),
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.LeftHand, left),
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.RightHand, right),
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.Ground, root.transform.Find("AbilityAnchor_Ground"))
                });
                BossAbilityPresenter presenter = GetOrAdd<BossAbilityPresenter>(root);
                SetObject(presenter, "stateReplicator", abilityReplicator); SetObject(presenter, "phaseProvider", phaseProvider);
                SetObject(presenter, "anchors", anchors);
                BossAbilityVfxPresenter vfx = GetOrAdd<BossAbilityVfxPresenter>(root); SetObject(vfx, "cueSource", presenter);
                BossAreaTelegraphVfxPresenter areaVfx = GetOrAdd<BossAreaTelegraphVfxPresenter>(root);
                SetObject(areaVfx, "source", areaTelegraph); SetObject(areaVfx, "phaseProvider", phaseProvider);
                BossSweepWaveVfxPresenter sweepVfx = GetOrAdd<BossSweepWaveVfxPresenter>(root);
                SetObject(sweepVfx, "source", sweepTelegraph); SetObject(sweepVfx, "phaseProvider", phaseProvider);
                BossTrackingLaserVfxPresenter laserVfx = GetOrAdd<BossTrackingLaserVfxPresenter>(root);
                SetObject(laserVfx, "source", laserPresentation); SetObject(laserVfx, "phaseProvider", phaseProvider);
                BossAbilityAnimatorPresenter animator = GetOrAdd<BossAbilityAnimatorPresenter>(root);
                SetObject(animator, "cueSource", presenter); SetObject(animator, "animator", root.GetComponentInChildren<Animator>());
                AudioSource audioSource = GetOrAdd<AudioSource>(root); audioSource.spatialBlend = 1f; audioSource.playOnAwake = false;
                BossAbilityAudioPresenter audio = GetOrAdd<BossAbilityAudioPresenter>(root);
                SetObject(audio, "cueSource", presenter); SetObject(audio, "audioSource", audioSource);

                PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        }

        private static void ConfigureProjectileCatalog(BossProjectileSpawner spawner, GameObject prefab)
        {
            var so = new SerializedObject(spawner);
            SerializedProperty rows = so.FindProperty("projectiles");
            rows.arraySize = 1;
            SerializedProperty row = rows.GetArrayElementAtIndex(0);
            row.FindPropertyRelative("projectileId").uintValue = 1;
            row.FindPropertyRelative("prefab").objectReferenceValue = prefab.GetComponent<NetworkObject>();
            row.FindPropertyRelative("hitMask").intValue = 1 << 3;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PlaceBossInScene(GameObject bossPrefab)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool loaded = scene.IsValid() && scene.isLoaded;
            if (!loaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject instance = null;
                GameObject legacyBoss = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == "Boss_AbilitySystemDemo" || root.name == "Boss_Encounter" ||
                        PrefabUtility.GetCorrespondingObjectFromSource(root) == bossPrefab)
                    { instance = root; break; }

                    if (root.name == "Boss_BloodLord") legacyBoss = root;
                }

                if (instance == null)
                {
                    Vector3 position = legacyBoss != null ? legacyBoss.transform.position : Vector3.zero;
                    Quaternion rotation = legacyBoss != null ? legacyBoss.transform.rotation : Quaternion.identity;
                    Vector3 scale = legacyBoss != null ? legacyBoss.transform.localScale : Vector3.one;
                    int siblingIndex = legacyBoss != null ? legacyBoss.transform.GetSiblingIndex() : -1;

                    instance = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab, scene);
                    instance.transform.SetPositionAndRotation(position, rotation);
                    instance.transform.localScale = scale;
                    if (siblingIndex >= 0) instance.transform.SetSiblingIndex(siblingIndex);
                }

                instance.name = "Boss_Encounter";

                // The legacy root has no external scene references and contains the old,
                // non-authoritative controller stack. Keeping it would spawn two bosses and
                // allow the old scripts to fight the new server-authoritative encounter.
                if (legacyBoss != null && legacyBoss != instance)
                    Object.DestroyImmediate(legacyBoss);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void ConfigureCue(SerializedProperty cue, string name, float time,
            BossAbilityAnchorId anchor, GameObject vfx, Vector3 scale, float lifetime,
            BossAbilityCueSpawnMode spawnMode = BossAbilityCueSpawnMode.SingleAnchor)
        {
            cue.FindPropertyRelative("timeFromCastStart").floatValue = time;
            cue.FindPropertyRelative("cueName").stringValue = name;
            cue.FindPropertyRelative("anchor").enumValueIndex = (int)anchor;
            cue.FindPropertyRelative("localPosition").vector3Value = Vector3.up * .05f;
            cue.FindPropertyRelative("localEulerAngles").vector3Value = Vector3.zero;
            cue.FindPropertyRelative("localScale").vector3Value = scale;
            cue.FindPropertyRelative("followAnchor").boolValue = anchor != BossAbilityAnchorId.Ground;
            cue.FindPropertyRelative("spawnMode").enumValueIndex = (int)spawnMode;
            cue.FindPropertyRelative("lifetime").floatValue = Mathf.Max(0f, lifetime);
            cue.FindPropertyRelative("vfxPrefab").objectReferenceValue = vfx;
            cue.FindPropertyRelative("animatorTrigger").stringValue = string.Empty;
            cue.FindPropertyRelative("audioClip").objectReferenceValue = null;
            cue.FindPropertyRelative("audioVolume").floatValue = 1f;
        }

        private static void ConfigureAtlasImporter()
        {
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
            if (!(AssetImporter.GetAtPath(AtlasPath) is TextureImporter importer)) return;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabListPath);
            if (list == null || prefab == null) return;
            var so = new SerializedObject(list);
            SerializedProperty rows = so.FindProperty("List");
            for (int i = 0; i < rows.arraySize; i++)
                if (rows.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab) return;
            rows.InsertArrayElementAtIndex(rows.arraySize);
            SerializedProperty row = rows.GetArrayElementAtIndex(rows.arraySize - 1);
            row.FindPropertyRelative("Override").enumValueIndex = 0;
            row.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            row.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
            row.FindPropertyRelative("SourceHashToOverride").ulongValue = 0;
            row.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(list);
        }

        private static void StripToVisualOnly(GameObject root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null || component is Transform || component is Renderer ||
                    component is MeshFilter || component is Animator) continue;
                Object.DestroyImmediate(component);
            }
        }

        private static string CellName(int cell)
        {
            string[] names = { "Sweep", "RadialVolley", "Bombardment", "ChargeSlash", "GridCut",
                "LaserSweep", "StaggerShockwave", "FrenzySiphon", "PhaseTransitionGold" };
            return names[Mathf.Clamp(cell, 0, names.Length - 1)];
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component =>
            target.TryGetComponent(out T component) ? component : target.AddComponent<T>();

        private static T GetOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static MonoScript FindScriptForType(Type type)
        {
            foreach (MonoScript script in MonoImporter.GetAllRuntimeMonoScripts())
                if (script != null && script.GetClass() == type) return script;
            throw new InvalidOperationException($"Logic script not found for {type.FullName}");
        }

        private static Transform GetOrCreateAnchor(Transform parent, string name, Vector3 position)
        {
            Transform anchor = parent.Find(name);
            if (anchor == null) { anchor = new GameObject(name).transform; anchor.SetParent(parent, false); }
            anchor.localPosition = position; anchor.localRotation = Quaternion.identity; anchor.localScale = Vector3.one;
            return anchor;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void SetObject(Object target, string name, Object value)
        { var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetBoolean(Object target, string name, bool value)
        { var so = new SerializedObject(target); so.FindProperty(name).boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetInteger(Object target, string name, int value)
        { var so = new SerializedObject(target); so.FindProperty(name).intValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetFloat(Object target, string name, float value)
        { var so = new SerializedObject(target); so.FindProperty(name).floatValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
