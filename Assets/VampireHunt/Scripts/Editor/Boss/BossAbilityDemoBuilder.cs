using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VampireHunt.Boss.Abilities.Logic;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;
using VampireHunt.Presentation.Boss;
using Object = UnityEngine.Object;

namespace VampireHunt.Editor.Boss
{
    public static class BossAbilityDemoBuilder
    {
        private const string DataRoot = "Assets/VampireHunt/Data/Boss";
        private const string AbilityRoot = DataRoot + "/Skills";
        private const string PhaseRoot = DataRoot + "/Phases";
        private const string PrefabRoot = "Assets/VampireHunt/Prefabs/Boss";
        private const string VfxRoot = PrefabRoot + "/BossVFX";
        private const string PresentationRoot = "Assets/VampireHunt/Presentation/Boss/Demo";
        private const string AbilityPath = AbilityRoot + "/VH_Boss_TestPulse.asset";
        private const string PhasePath = PhaseRoot + "/VH_Boss_DemoPhase1.asset";
        private const string PhaseSetPath = PhaseRoot + "/VH_Boss_DemoPhases.asset";
        private const string MaterialPath = PresentationRoot + "/VH_Boss_TestPulse.mat";
        private const string LegacyMaterialPath = DataRoot + "/VH_Boss_TestPulse.mat";
        private const string VfxPath = VfxRoot + "/VH_Boss_TestPulse.prefab";
        private const string BossPrefabPath = PrefabRoot + "/VH_Boss.prefab";
        private const string NetworkPrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string DemoScenePath = "Assets/VampireHunt/Scenes/VampireHunt.unity";

        [MenuItem("Tools/Vampire Hunt/Boss/Create Ability System Demo", priority = 200)]
        public static void CreateDemo()
        {
            EnsureFolder(DataRoot);
            EnsureFolder(AbilityRoot);
            EnsureFolder(PhaseRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder(VfxRoot);
            EnsureFolder(PresentationRoot);

            Material material = CreateOrUpdateMaterial();
            GameObject vfxPrefab = CreateOrUpdateVfx(material);
            MonoScript logicScript = FindScriptForType(typeof(BossNoOpAbilityLogic));
            BossAbilityAsset ability = GetOrCreateAsset<BossAbilityAsset>(AbilityPath);
            ConfigureAbility(ability, logicScript, vfxPrefab);
            BossPhaseAsset phase = GetOrCreateAsset<BossPhaseAsset>(PhasePath);
            ConfigurePhase(phase, ability);
            BossPhaseSetAsset phaseSet = GetOrCreateAsset<BossPhaseSetAsset>(PhaseSetPath);
            ConfigurePhaseSet(phaseSet, phase);
            GameObject bossPrefab = CreateOrUpdateBossPrefab(phaseSet);
            RegisterNetworkPrefab(bossPrefab);
            PlaceDemoBossInScene(bossPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = bossPrefab;
            EditorGUIUtility.PingObject(bossPrefab);
            Debug.Log(
                "[BossAbilityDemoBuilder] Demo created. Open VampireHunt scene and enter Play Mode; " +
                "the configured Boss prefab automatically loops the presentation-only test ability.",
                bossPrefab);
        }

        [MenuItem("Tools/Vampire Hunt/Boss/Validate Ability Content", priority = 201)]
        public static void ValidateContent()
        {
            string[] abilityGuids = AssetDatabase.FindAssets("t:BossAbilityAsset", new[] { "Assets/VampireHunt/Data/Boss" });
            var usedIds = new System.Collections.Generic.Dictionary<uint, string>();
            int errors = 0;

            for (int i = 0; i < abilityGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(abilityGuids[i]);
                BossAbilityAsset ability = AssetDatabase.LoadAssetAtPath<BossAbilityAsset>(path);
                if (ability == null) continue;

                if (usedIds.TryGetValue(ability.AbilityId, out string existing))
                {
                    Debug.LogError($"Duplicate Boss Ability ID {ability.AbilityId}: {existing} and {path}", ability);
                    errors++;
                }
                else
                {
                    usedIds.Add(ability.AbilityId, path);
                }

                if (!BossAbilityAssetEditor.TryGetLogicType(
                        ability.LogicScript as MonoScript,
                        out _,
                        out string logicError))
                {
                    Debug.LogError($"Boss Ability {ability.name}: {logicError}", ability);
                    errors++;
                }

                for (int cueIndex = 0; cueIndex < ability.PresentationCues.Count; cueIndex++)
                {
                    BossAbilityPresentationCue cue = ability.PresentationCues[cueIndex];
                    if (cue == null || cue.TimeFromCastStart <= ability.TotalDuration) continue;
                    Debug.LogWarning(
                        $"Boss Ability {ability.name} cue {cue.CueName} occurs after the cast has finished.",
                        ability);
                }
            }

            if (errors == 0)
                Debug.Log($"[BossAbilityContentValidator] Validated {abilityGuids.Length} Boss Ability assets without ID errors.");
        }

        private static Material CreateOrUpdateMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null && AssetDatabase.LoadAssetAtPath<Material>(LegacyMaterialPath) != null)
            {
                string moveError = AssetDatabase.MoveAsset(LegacyMaterialPath, MaterialPath);
                if (!string.IsNullOrEmpty(moveError))
                    throw new InvalidOperationException($"Could not move Boss demo material: {moveError}");
                material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                            Shader.Find("Particles/Standard Unlit") ??
                            Shader.Find("Sprites/Default");
            if (material == null)
            {
                material = new Material(shader) { name = "VH_Boss_TestPulse" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            Color bloodRed = new Color(1f, 0.02f, 0.08f, 0.9f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", bloodRed);
            if (material.HasProperty("_Color")) material.SetColor("_Color", bloodRed);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreateOrUpdateVfx(Material material)
        {
            var root = new GameObject("VH_Boss_TestPulse");
            try
            {
                ParticleSystem particleSystem = root.AddComponent<ParticleSystem>();
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particleSystem.main;
                main.duration = 0.8f;
                main.loop = false;
                main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.45f, 0f, 0.02f, 0.8f),
                    new Color(1f, 0.04f, 0.1f, 1f));
                main.simulationSpace = ParticleSystemSimulationSpace.Local;

                var emission = particleSystem.emission;
                emission.rateOverTime = 45f;

                var shape = particleSystem.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.45f;

                var colorOverLifetime = particleSystem.colorOverLifetime;
                colorOverLifetime.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1f, 0.05f, 0.12f), 0f),
                        new GradientColorKey(new Color(0.3f, 0f, 0.02f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0f, 1f)
                    });
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

                ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;

                Light light = root.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0f, 0.04f);
                light.intensity = 3f;
                light.range = 3f;

                return PrefabUtility.SaveAsPrefabAsset(root, VfxPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GameObject CreateOrUpdateBossPrefab(BossPhaseSetAsset phaseSet)
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
            if (prefabAsset == null)
                throw new InvalidOperationException($"Boss prefab is missing: {BossPrefabPath}");

            GameObject root = PrefabUtility.LoadPrefabContents(BossPrefabPath);
            try
            {
                root.name = "Boss";
                GetOrAdd<NetworkObject>(root);
                GetOrAdd<NetworkTransform>(root);

                Bounds localBounds = CalculateLocalVisualBounds(root);
                float height = Mathf.Max(1f, localBounds.size.y);
                float width = Mathf.Max(0.8f, localBounds.size.x);
                CharacterController controller = GetOrAdd<CharacterController>(root);
                controller.height = height;
                controller.radius = Mathf.Max(0.3f, width * 0.25f);
                controller.center = localBounds.center;

                AudioSource audioSource = GetOrAdd<AudioSource>(root);
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
                audioSource.maxDistance = 30f;

                float bottom = localBounds.min.y;
                Transform chest = GetOrCreateAnchor(
                    root.transform,
                    "AbilityAnchor_Chest",
                    new Vector3(localBounds.center.x, bottom + height * 0.62f, localBounds.center.z));
                Transform head = GetOrCreateAnchor(
                    root.transform,
                    "AbilityAnchor_Head",
                    new Vector3(localBounds.center.x, bottom + height * 0.9f, localBounds.center.z));
                Transform leftHand = GetOrCreateAnchor(
                    root.transform,
                    "AbilityAnchor_LeftHand",
                    new Vector3(localBounds.center.x - width * 0.48f, bottom + height * 0.58f, localBounds.center.z));
                Transform rightHand = GetOrCreateAnchor(
                    root.transform,
                    "AbilityAnchor_RightHand",
                    new Vector3(localBounds.center.x + width * 0.48f, bottom + height * 0.58f, localBounds.center.z));
                Transform ground = GetOrCreateAnchor(
                    root.transform,
                    "AbilityAnchor_Ground",
                    new Vector3(localBounds.center.x, bottom, localBounds.center.z));

                BossAbilityAnchorRegistry anchorRegistry = GetOrAdd<BossAbilityAnchorRegistry>(root);
                anchorRegistry.EditorSetBindings(new[]
                {
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.Chest, chest),
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.Head, head),
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.LeftHand, leftHand),
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.RightHand, rightHand),
                    new BossAbilityAnchorRegistry.Binding(BossAbilityAnchorId.Ground, ground)
                });

                BossAbilityPhaseProvider phaseProvider = GetOrAdd<BossAbilityPhaseProvider>(root);
                SetObjectReference(phaseProvider, "phaseSet", phaseSet);
                SetInteger(phaseProvider, "startingPhase", 1);

                BossAbilityContextProvider contextProvider = GetOrAdd<BossAbilityContextProvider>(root);
                SetBoolean(contextProvider, "useForwardPreviewWhenTargetIsMissing", true);
                SetFloat(contextProvider, "previewTargetDistance", 5f);
                SetFloat(contextProvider, "normalizedBossHealth", 1f);

                // Atomic server adapters used by gameplay logic. The service host only composes them.
                GetOrAdd<BossPlayerTargetQuery>(root);
                GetOrAdd<BossPhysicsHitQuery>(root);
                GetOrAdd<BossDamageService>(root);
                GetOrAdd<BossProjectileSpawner>(root);
                GetOrAdd<BossStatusEffectService>(root);
                GetOrAdd<BossRunClockModifier>(root);
                GetOrAdd<BossBodyStateHost>(root);
                BossAbilityServiceHost serviceHost = GetOrAdd<BossAbilityServiceHost>(root);

                BossAbilityHost host = GetOrAdd<BossAbilityHost>(root);
                SetObjectReference(host, "serviceHost", serviceHost);

                BossAbilityStateReplicator replicator = GetOrAdd<BossAbilityStateReplicator>(root);

                BossAbilityServerDriver serverDriver = GetOrAdd<BossAbilityServerDriver>(root);
                SetObjectReference(serverDriver, "host", host);
                SetObjectReference(serverDriver, "phaseProvider", phaseProvider);
                SetObjectReference(serverDriver, "contextProvider", contextProvider);
                SetObjectReference(serverDriver, "stateReplicator", replicator);
                SetBoolean(serverDriver, "allowAutomaticCasts", true);
                SetInteger(serverDriver, "runSeed", 1337);
                SetBoolean(serverDriver, "offlinePreview", true);

                BossAbilityPresenter presenter = GetOrAdd<BossAbilityPresenter>(root);
                SetObjectReference(presenter, "stateReplicator", replicator);
                SetObjectReference(presenter, "phaseProvider", phaseProvider);
                SetObjectReference(presenter, "anchors", anchorRegistry);

                BossAbilityAnimatorPresenter animatorPresenter = GetOrAdd<BossAbilityAnimatorPresenter>(root);
                SetObjectReference(animatorPresenter, "cueSource", presenter);
                SetObjectReference(animatorPresenter, "animator", root.GetComponentInChildren<Animator>(true));

                BossAbilityVfxPresenter vfxPresenter = GetOrAdd<BossAbilityVfxPresenter>(root);
                SetObjectReference(vfxPresenter, "cueSource", presenter);

                BossAbilityAudioPresenter audioPresenter = GetOrAdd<BossAbilityAudioPresenter>(root);
                SetObjectReference(audioPresenter, "cueSource", presenter);
                SetObjectReference(audioPresenter, "audioSource", audioSource);

                PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        }

        private static void ConfigureAbility(
            BossAbilityAsset ability,
            MonoScript logicScript,
            GameObject vfxPrefab)
        {
            var serialized = new SerializedObject(ability);
            serialized.FindProperty("abilityId").uintValue = 9001;
            serialized.FindProperty("displayName").stringValue = "Demo Blood Pulse";
            serialized.FindProperty("description").stringValue =
                "Presentation-only demo. It proves asset loading, server timeline replication and Boss-relative cues.";
            serialized.FindProperty("baseWeight").floatValue = 1f;
            serialized.FindProperty("cooldown").floatValue = 1.5f;
            serialized.FindProperty("minDistance").floatValue = 0f;
            serialized.FindProperty("maxDistance").floatValue = 100f;
            serialized.FindProperty("minNormalizedHealth").floatValue = 0f;
            serialized.FindProperty("maxNormalizedHealth").floatValue = 1f;
            serialized.FindProperty("requiresTarget").boolValue = false;
            serialized.FindProperty("oneShot").boolValue = false;
            serialized.FindProperty("telegraphDuration").floatValue = 0.75f;
            serialized.FindProperty("resolveDuration").floatValue = 0.25f;
            serialized.FindProperty("recoverDuration").floatValue = 0.75f;
            serialized.FindProperty("logicScript").objectReferenceValue = logicScript;
            serialized.FindProperty("logicTypeName").stringValue =
                typeof(BossNoOpAbilityLogic).AssemblyQualifiedName;

            SerializedProperty cues = serialized.FindProperty("presentationCues");
            cues.arraySize = 2;
            ConfigureCue(
                cues.GetArrayElementAtIndex(0),
                "Telegraph - Chest Blood Charge",
                0f,
                BossAbilityAnchorId.Chest,
                vfxPrefab,
                Vector3.zero,
                Vector3.one * 0.75f,
                true,
                0.9f);
            ConfigureCue(
                cues.GetArrayElementAtIndex(1),
                "Resolve - Ground Blood Pulse",
                0.75f,
                BossAbilityAnchorId.Ground,
                vfxPrefab,
                new Vector3(0f, 0.1f, 0f),
                Vector3.one * 2.5f,
                false,
                1.1f);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ability);
        }

        private static void ConfigureCue(
            SerializedProperty cue,
            string name,
            float time,
            BossAbilityAnchorId anchor,
            GameObject vfxPrefab,
            Vector3 localPosition,
            Vector3 localScale,
            bool followAnchor,
            float lifetime)
        {
            cue.FindPropertyRelative("timeFromCastStart").floatValue = time;
            cue.FindPropertyRelative("cueName").stringValue = name;
            cue.FindPropertyRelative("anchor").enumValueIndex = (int)anchor;
            cue.FindPropertyRelative("localPosition").vector3Value = localPosition;
            cue.FindPropertyRelative("localEulerAngles").vector3Value = Vector3.zero;
            cue.FindPropertyRelative("localScale").vector3Value = localScale;
            cue.FindPropertyRelative("followAnchor").boolValue = followAnchor;
            cue.FindPropertyRelative("lifetime").floatValue = lifetime;
            cue.FindPropertyRelative("keepAliveAfterCastEnd").boolValue = false;
            cue.FindPropertyRelative("animatorTrigger").stringValue = string.Empty;
            cue.FindPropertyRelative("vfxPrefab").objectReferenceValue = vfxPrefab;
            cue.FindPropertyRelative("audioClip").objectReferenceValue = null;
            cue.FindPropertyRelative("audioVolume").floatValue = 1f;
        }

        private static void ConfigurePhase(BossPhaseAsset phase, BossAbilityAsset ability)
        {
            var serialized = new SerializedObject(phase);
            serialized.FindProperty("phaseNumber").intValue = 1;
            serialized.FindProperty("displayName").stringValue = "Demo Phase 1";
            SerializedProperty abilities = serialized.FindProperty("abilities");
            abilities.arraySize = 1;
            SerializedProperty entry = abilities.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("enabled").boolValue = true;
            entry.FindPropertyRelative("ability").objectReferenceValue = ability;
            entry.FindPropertyRelative("weightMultiplier").floatValue = 1f;
            entry.FindPropertyRelative("maxUses").intValue = 0;
            entry.FindPropertyRelative("initialCooldown").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(phase);
        }

        private static void ConfigurePhaseSet(BossPhaseSetAsset phaseSet, BossPhaseAsset phase)
        {
            var serialized = new SerializedObject(phaseSet);
            SerializedProperty phases = serialized.FindProperty("phases");
            phases.arraySize = 1;
            phases.GetArrayElementAtIndex(0).objectReferenceValue = phase;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(phaseSet);
        }

        private static void RegisterNetworkPrefab(GameObject bossPrefab)
        {
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabListPath);
            if (list == null)
            {
                Debug.LogWarning($"Network prefab list not found: {NetworkPrefabListPath}", bossPrefab);
                return;
            }

            var serialized = new SerializedObject(list);
            SerializedProperty entries = serialized.FindProperty("List");
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == bossPrefab)
                    return;
            }

            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Override").enumValueIndex = 0;
            entry.FindPropertyRelative("Prefab").objectReferenceValue = bossPrefab;
            entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
            entry.FindPropertyRelative("SourceHashToOverride").ulongValue = 0;
            entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(list);
        }

        private static void PlaceDemoBossInScene(GameObject bossPrefab)
        {
            Scene scene = SceneManager.GetSceneByPath(DemoScenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive);

            try
            {
                GameObject existing = null;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i].name != "Boss_AbilitySystemDemo") continue;
                    existing = roots[i];
                    break;
                }

                if (existing == null)
                {
                    existing = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab, scene);
                    existing.name = "Boss_AbilitySystemDemo";
                    existing.transform.position = new Vector3(4f, 0f, 4f);
                    existing.transform.rotation = Quaternion.Euler(0f, 225f, 0f);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        private static Bounds CalculateLocalVisualBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f));

            Vector3 firstMin = root.transform.InverseTransformPoint(renderers[0].bounds.min);
            Vector3 firstMax = root.transform.InverseTransformPoint(renderers[0].bounds.max);
            Bounds bounds = new Bounds((firstMin + firstMax) * 0.5f, Abs(firstMax - firstMin));
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(root.transform.InverseTransformPoint(renderers[i].bounds.min));
                bounds.Encapsulate(root.transform.InverseTransformPoint(renderers[i].bounds.max));
            }
            return bounds;
        }

        private static Transform GetOrCreateAnchor(Transform parent, string name, Vector3 localPosition)
        {
            Transform anchor = parent.Find(name);
            if (anchor == null)
            {
                anchor = new GameObject(name).transform;
                anchor.SetParent(parent, false);
            }
            anchor.localPosition = localPosition;
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            return anchor;
        }

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

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
            MonoScript[] scripts = MonoImporter.GetAllRuntimeMonoScripts();
            for (int i = 0; i < scripts.Length; i++)
            {
                if (scripts[i] != null && scripts[i].GetClass() == type) return scripts[i];
            }

            throw new InvalidOperationException($"Could not find the .cs script for {type.FullName}.");
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBoolean(Object target, string propertyName, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInteger(Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
