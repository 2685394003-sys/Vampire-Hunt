using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VampireHunt.Bootstrap;

namespace VampireHunt.Tests.Architecture
{
    public sealed class ArchitectureBoundaryTests
    {
        private const string RuntimeRoot = "Assets/Scripts/VampireHunt";

        private static readonly string[] ForbiddenLogicTokens =
        {
            "UnityEngine.UI",
            "TMPro",
            "Animator",
            "ParticleSystem",
            "AudioSource",
            "Cinemachine",
            "Camera.main",
            "GameObject.Find",
            "FindObjectOfType",
            "FindFirstObjectByType",
            "FindAnyObjectByType",
            "NetworkManager.Singleton",
            "Unity.Netcode",
            ".Instance"
        };

        private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedDependencies =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["VampireHunt.Core"] = Set(),
                ["VampireHunt.Stats"] = Set("VampireHunt.Core"),
                ["VampireHunt.Combat"] = Set("VampireHunt.Core", "VampireHunt.Stats"),
                ["VampireHunt.Abilities"] = Set("VampireHunt.Core", "VampireHunt.Stats", "VampireHunt.Combat"),
                ["VampireHunt.Navigation"] = Set("VampireHunt.Core"),
                ["VampireHunt.Player"] = Set("VampireHunt.Core", "VampireHunt.Stats", "VampireHunt.Combat", "VampireHunt.Abilities"),
                ["VampireHunt.Enemies"] = Set("VampireHunt.Core", "VampireHunt.Stats", "VampireHunt.Combat", "VampireHunt.Abilities", "VampireHunt.Navigation"),
                ["VampireHunt.Spawning"] = Set("VampireHunt.Core", "VampireHunt.Navigation"),
                ["VampireHunt.Boss"] = Set("VampireHunt.Core", "VampireHunt.Stats", "VampireHunt.Combat", "VampireHunt.Abilities", "VampireHunt.Navigation"),
                ["VampireHunt.Infrastructure.Input"] = Set("VampireHunt.Core", "VampireHunt.Player"),
                ["VampireHunt.Infrastructure.Integration"] = Set(
                    "VampireHunt.Core",
                    "VampireHunt.Stats",
                    "VampireHunt.Combat",
                    "VampireHunt.Abilities",
                    "VampireHunt.Player",
                    "VampireHunt.Enemies",
                    "VampireHunt.Boss",
                    "VampireHunt.Spawning"),
                ["VampireHunt.Infrastructure.Netcode"] = Set(
                    "VampireHunt.Core",
                    "VampireHunt.Player",
                    "VampireHunt.Enemies",
                    "VampireHunt.Boss",
                    "VampireHunt.Spawning",
                    "VampireHunt.Combat",
                    "VampireHunt.Abilities"),
                ["VampireHunt.Infrastructure.UnityPhysics"] = Set(
                    "VampireHunt.Core",
                    "VampireHunt.Navigation",
                    "VampireHunt.Combat",
                    "VampireHunt.Player",
                    "VampireHunt.Enemies",
                    "VampireHunt.Boss",
                    "VampireHunt.Spawning"),
                ["VampireHunt.Presentation"] = Set(
                    "VampireHunt.Core",
                    "VampireHunt.Combat",
                    "VampireHunt.Abilities",
                    "VampireHunt.Player",
                    "VampireHunt.Enemies",
                    "VampireHunt.Boss",
                    "VampireHunt.Navigation"),
                ["VampireHunt.UI"] = Set("VampireHunt.Core", "VampireHunt.Player"),
                ["VampireHunt.Bootstrap"] = Set(
                    "VampireHunt.Core",
                    "VampireHunt.Stats",
                    "VampireHunt.Combat",
                    "VampireHunt.Abilities",
                    "VampireHunt.Navigation",
                    "VampireHunt.Player",
                    "VampireHunt.Enemies",
                    "VampireHunt.Boss",
                    "VampireHunt.Spawning",
                    "VampireHunt.Infrastructure.Input",
                    "VampireHunt.Infrastructure.Integration",
                    "VampireHunt.Infrastructure.Netcode",
                    "VampireHunt.Infrastructure.UnityPhysics",
                    "VampireHunt.Presentation",
                    "VampireHunt.UI")
            };

        [Test]
        public void RuntimeAssemblyGraph_IsAcyclicAndRespectsFeatureDependencies()
        {
            Dictionary<string, AssemblyDefinitionData> definitions = LoadRuntimeAssemblyDefinitions();
            Assert.That(definitions, Is.Not.Empty, $"No asmdef files found below {RuntimeRoot}.");

            foreach (KeyValuePair<string, AssemblyDefinitionData> pair in definitions)
            {
                string assemblyName = pair.Key;
                AssemblyDefinitionData definition = pair.Value;
                Assert.That(
                    AllowedDependencies.TryGetValue(assemblyName, out HashSet<string> allowed),
                    Is.True,
                    $"No dependency policy exists for runtime assembly {assemblyName}.");

                string[] unexpected = definition.References
                    .Where(reference => reference.StartsWith("VampireHunt.", StringComparison.Ordinal))
                    .Where(reference => !allowed.Contains(reference))
                    .OrderBy(reference => reference, StringComparer.Ordinal)
                    .ToArray();

                Assert.That(
                    unexpected,
                    Is.Empty,
                    $"{assemblyName} has forbidden project dependencies: {string.Join(", ", unexpected)}");
            }

            AssertGraphIsAcyclic(definitions);
        }

        [Test]
        public void RuntimeAssemblyDependencyPolicy_CoversEveryRuntimeAssembly()
        {
            Dictionary<string, AssemblyDefinitionData> definitions = LoadRuntimeAssemblyDefinitions();
            string[] missingPolicies = definitions.Keys
                .Where(assemblyName => !AllowedDependencies.ContainsKey(assemblyName))
                .OrderBy(assemblyName => assemblyName, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                missingPolicies,
                Is.Empty,
                "Every runtime asmdef must fail closed under an explicit dependency policy: " +
                string.Join(", ", missingPolicies));
        }

        [Test]
        public void DomainAndApplicationSources_DoNotReferencePresentationOrRuntimeLocators()
        {
            foreach (string file in EnumerateLogicFiles())
            {
                string source = File.ReadAllText(file);
                foreach (string token in ForbiddenLogicTokens)
                {
                    Assert.That(
                        source.IndexOf(token, StringComparison.Ordinal) >= 0,
                        Is.False,
                        $"{ToAssetPath(file)} contains forbidden logic dependency '{token}'.");
                }
            }
        }

        [Test]
        public void DomainAndApplicationSources_DoNotConsumeAuthoringNamespaces()
        {
            Regex authoringUsing = new(@"^\s*using\s+VampireHunt\.[A-Za-z0-9_.]+\.Authoring\s*;", RegexOptions.Multiline);

            foreach (string file in EnumerateLogicFiles())
            {
                string source = File.ReadAllText(file);
                Assert.That(
                    authoringUsing.IsMatch(source),
                    Is.False,
                    $"{ToAssetPath(file)} imports an Authoring namespace.");
            }
        }

        [Test]
        public void RuntimeConsumers_ReferenceFeatureContractsOnly()
        {
            string[] consumerRoots =
            {
                "Assets/Scripts/VampireHunt/Infrastructure/Integration",
                "Assets/Scripts/VampireHunt/Infrastructure/Netcode",
                "Assets/Scripts/VampireHunt/Presentation",
                "Assets/Scripts/VampireHunt/UI"
            };
            Regex forbiddenFeatureReference = new(
                @"\bVampireHunt\.(?:Player|Enemies|Boss|Spawning|Navigation|Abilities)\.(?!Contracts\b)[A-Za-z_][A-Za-z0-9_.]*",
                RegexOptions.CultureInvariant);

            foreach (string consumerRoot in consumerRoots)
            {
                string absoluteRoot = ToAbsolutePath(consumerRoot);
                if (!Directory.Exists(absoluteRoot)) continue;
                foreach (string file in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
                {
                    string source = File.ReadAllText(file);
                    Match match = forbiddenFeatureReference.Match(source);
                    Assert.That(
                        match.Success,
                        Is.False,
                        $"{ToAssetPath(file)} bypasses a feature Contracts namespace with '{match.Value}'.");
                }
            }
        }

        [Test]
        public void FeatureAggregates_AreInternalImplementationDetails()
        {
            Regex publicAggregate = new(
                @"\bpublic\s+(?:sealed\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*Aggregate)\b",
                RegexOptions.CultureInvariant);
            List<string> exposed = new();
            string runtimeRoot = ToAbsolutePath(RuntimeRoot);

            foreach (string file in Directory.EnumerateFiles(runtimeRoot, "*Aggregate.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                Match match = publicAggregate.Match(source);
                if (match.Success)
                    exposed.Add($"{ToAssetPath(file)} exposes {match.Groups[1].Value}");
            }

            Assert.That(
                exposed,
                Is.Empty,
                "Feature Aggregates must remain internal; cross-module access goes through Contracts: " +
                string.Join("; ", exposed));
        }

        [Test]
        public void LegacyPlayerShell_DoesNotOwnTheApplicationSimulationTick()
        {
            string shellPath = ToAbsolutePath("Assets/Scripts/Player/PlayerNetworkState.cs");
            Assert.That(File.Exists(shellPath), Is.True, "PlayerNetworkState compatibility shell is missing.");

            string source = File.ReadAllText(shellPath);
            Assert.That(
                source.IndexOf("runtime.Tick(Time.deltaTime)", StringComparison.Ordinal) >= 0,
                Is.False,
                "Bootstrap owns the fixed simulation loop; PlayerNetworkState must not tick the same runtime again.");
        }

        [Test]
        public void NetworkPrefabs_HaveNoMissingScripts()
        {
            string prefabRoot = ToAbsolutePath("Assets/Prefabs/Network");
            Assert.That(Directory.Exists(prefabRoot), Is.True, "Network prefab root is missing.");
            List<string> missing = new();

            foreach (string file in Directory.EnumerateFiles(prefabRoot, "*.prefab", SearchOption.AllDirectories))
            {
                string assetPath = ToAssetPath(file);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    missing.Add($"{assetPath}: prefab could not be loaded");
                    continue;
                }

                Transform[] hierarchy = prefab.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < hierarchy.Length; i++)
                {
                    GameObject gameObject = hierarchy[i].gameObject;
                    int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                    if (count > 0)
                        missing.Add($"{assetPath}/{GetHierarchyPath(hierarchy[i])}: {count} missing script(s)");
                }
            }

            Assert.That(
                missing,
                Is.Empty,
                "Network prefabs must contain zero Missing Script components: " + string.Join("; ", missing));
        }

        [Test]
        public void RuntimeContent_HasNoUnresolvedMonoScripts()
        {
            List<string> unresolved = new();
            foreach (string assetPath in EnumerateRuntimeContentAssetPaths())
            {
                string extension = Path.GetExtension(assetPath);
                if (!string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(ToAbsolutePath(assetPath)))
                {
                    unresolved.Add($"{assetPath}: asset file is missing");
                    continue;
                }

                if (string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (prefab == null)
                        unresolved.Add($"{assetPath}: prefab could not be loaded");
                    else
                        CollectMissingScripts(prefab, assetPath, unresolved);
                    continue;
                }

                Scene scene = SceneManager.GetSceneByPath(assetPath);
                bool openedByTest = !scene.IsValid() || !scene.isLoaded;
                try
                {
                    if (openedByTest)
                        scene = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Additive);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        unresolved.Add($"{assetPath}: scene could not be loaded");
                        continue;
                    }

                    foreach (GameObject root in scene.GetRootGameObjects())
                        CollectMissingScripts(root, assetPath, unresolved);
                }
                finally
                {
                    if (openedByTest && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }

            Assert.That(
                unresolved,
                Is.Empty,
                "Enabled build scenes, Resources, Addressables and network prefabs must contain zero unresolved scripts: " +
                string.Join("; ", unresolved));
        }

        [Test]
        public void EnabledBuildScenes_NetworkManagerSubtreesContainNoNetworkObjectsOrBehaviours()
        {
            List<string> violations = new();

            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (!buildScene.enabled || string.IsNullOrWhiteSpace(buildScene.path))
                    continue;

                Scene scene = SceneManager.GetSceneByPath(buildScene.path);
                bool openedByTest = !scene.IsValid() || !scene.isLoaded;
                try
                {
                    if (openedByTest)
                    {
                        scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);
                    }

                    Assert.That(
                        scene.IsValid() && scene.isLoaded,
                        Is.True,
                        $"Enabled build scene could not be loaded for NetworkManager topology validation: {buildScene.path}");

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        Component[] components = root.GetComponentsInChildren<Component>(true);
                        for (int i = 0; i < components.Length; i++)
                        {
                            Component component = components[i];
                            if (component == null || !IsNetcodeType(component, "Unity.Netcode.NetworkManager"))
                                continue;

                            Component[] descendants = component.GetComponentsInChildren<Component>(true);
                            for (int j = 0; j < descendants.Length; j++)
                            {
                                Component descendant = descendants[j];
                                if (descendant == null)
                                    continue;

                                if (IsNetcodeType(descendant, "Unity.Netcode.NetworkObject") ||
                                    IsNetcodeType(descendant, "Unity.Netcode.NetworkBehaviour"))
                                {
                                    violations.Add(
                                        $"{buildScene.path}/{GetHierarchyPath(descendant.transform)} " +
                                        $"contains {descendant.GetType().FullName} below NetworkManager.");
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (openedByTest && scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "NetworkManager roots and all of their descendants must not contain NetworkObject or " +
                "NetworkBehaviour components: " + string.Join("; ", violations));
        }

        private static void CollectMissingScripts(
            GameObject root,
            string assetPath,
            ICollection<string> unresolved)
        {
            Transform[] hierarchy = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < hierarchy.Length; i++)
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    hierarchy[i].gameObject);
                if (count > 0)
                    unresolved.Add(
                        $"{assetPath}/{GetHierarchyPath(hierarchy[i])}: {count} missing script(s)");
            }
        }

        private static bool IsNetcodeType(Component component, string baseTypeFullName)
        {
            for (Type current = component.GetType(); current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, baseTypeFullName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        [Test]
        public void RuntimeConfigCatalogs_ArePresentAndBuildValidSpecs()
        {
            string[] guids = AssetDatabase.FindAssets("t:ConfigCatalog");
            Assert.That(guids, Is.Not.Empty, "At least one runtime ConfigCatalog asset is required.");

            List<string> invalid = new();
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                ConfigCatalog catalog = AssetDatabase.LoadAssetAtPath<ConfigCatalog>(assetPath);
                if (catalog == null)
                {
                    invalid.Add($"{assetPath}: could not load ConfigCatalog");
                    continue;
                }

                try
                {
                    catalog.Validate();
                    Assert.That(catalog.BuildSpecs(), Is.Not.Null, $"{assetPath} returned no GameSpecs.");
                }
                catch (Exception exception)
                {
                    invalid.Add($"{assetPath}: {exception.GetType().Name}: {exception.Message}");
                }
            }

            Assert.That(
                invalid,
                Is.Empty,
                "Every runtime ConfigCatalog must complete the production builder path: " + string.Join("; ", invalid));
        }

        [Test]
        public void EveryClassDiagramType_HasAnImplementationDeclaration()
        {
            string designPath = ToAbsolutePath("Docs/Architecture/MODULE_DESIGN.md");
            Assert.That(File.Exists(designPath), Is.True, "MODULE_DESIGN.md is missing.");

            string design = File.ReadAllText(designPath);
            HashSet<string> required = new(
                Regex.Matches(
                        design,
                        @"^\s*class\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|$)",
                        RegexOptions.Multiline)
                    .Cast<Match>()
                    .Select(match => match.Groups[1].Value),
                StringComparer.Ordinal);

            HashSet<string> declared = new(StringComparer.Ordinal);
            string runtimeRoot = ToAbsolutePath(RuntimeRoot);
            foreach (string file in Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                foreach (Match match in Regex.Matches(
                             source,
                             @"\b(?:class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b"))
                {
                    declared.Add(match.Groups[1].Value);
                }
            }

            string[] missing = required
                .Where(typeName => !declared.Contains(typeName))
                .OrderBy(typeName => typeName, StringComparer.Ordinal)
                .ToArray();
            Assert.That(
                missing,
                Is.Empty,
                $"Class-diagram types without a source declaration: {string.Join(", ", missing)}");
        }

        private static IEnumerable<string> EnumerateRuntimeContentAssetPaths()
        {
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                    paths.Add(scene.path.Replace('\\', '/'));
            }

            foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
            {
                string normalized = assetPath.Replace('\\', '/');
                if (normalized.StartsWith("Assets/Prefabs/Network/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    paths.Add(normalized);
                }
            }

            AddAddressableAssetPaths(paths);
            return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static void AddAddressableAssetPaths(ISet<string> paths)
        {
            Type defaultObjectType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor",
                false);
            object settings = defaultObjectType?.GetProperty("Settings")?.GetValue(null);
            object groupsObject = settings?.GetType().GetProperty("groups")?.GetValue(settings);
            if (groupsObject is not IEnumerable groups) return;

            string[] allAssets = AssetDatabase.GetAllAssetPaths();
            foreach (object group in groups)
            {
                if (group == null) continue;
                object entriesObject = group.GetType().GetProperty("entries")?.GetValue(group);
                if (entriesObject is not IEnumerable entries) continue;

                foreach (object entry in entries)
                {
                    string assetPath = entry?.GetType().GetProperty("AssetPath")?.GetValue(entry) as string;
                    if (string.IsNullOrWhiteSpace(assetPath)) continue;
                    string normalized = assetPath.Replace('\\', '/');
                    if (!AssetDatabase.IsValidFolder(normalized))
                    {
                        paths.Add(normalized);
                        continue;
                    }

                    string prefix = normalized.TrimEnd('/') + "/";
                    foreach (string nestedAsset in allAssets)
                    {
                        if (nestedAsset.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            paths.Add(nestedAsset);
                    }
                }
            }
        }

        private static Dictionary<string, AssemblyDefinitionData> LoadRuntimeAssemblyDefinitions()
        {
            string absoluteRoot = ToAbsolutePath(RuntimeRoot);
            if (!Directory.Exists(absoluteRoot))
            {
                return new Dictionary<string, AssemblyDefinitionData>(StringComparer.Ordinal);
            }

            string[] files = Directory.EnumerateFiles(absoluteRoot, "*.asmdef", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Dictionary<string, string> guidToAssemblyName = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, AssemblyDefinitionJson> jsonByFile = new(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                AssemblyDefinitionJson json = JsonUtility.FromJson<AssemblyDefinitionJson>(File.ReadAllText(file));
                if (json == null || string.IsNullOrWhiteSpace(json.name))
                {
                    Assert.Fail($"Invalid asmdef JSON: {ToAssetPath(file)}");
                }

                string metaPath = file + ".meta";
                if (!File.Exists(metaPath))
                {
                    Assert.Fail($"Runtime asmdef is missing its .meta GUID: {ToAssetPath(file)}");
                }

                Match guidMatch = Regex.Match(
                    File.ReadAllText(metaPath),
                    @"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$");
                if (!guidMatch.Success)
                {
                    Assert.Fail($"Runtime asmdef has an invalid .meta GUID: {ToAssetPath(metaPath)}");
                }

                string guid = guidMatch.Groups[1].Value;
                if (!guidToAssemblyName.TryAdd(guid, json.name))
                {
                    Assert.Fail($"Duplicate runtime asmdef GUID {guid} ({ToAssetPath(file)}).");
                }

                jsonByFile.Add(file, json);
            }

            Dictionary<string, AssemblyDefinitionData> definitions = new(StringComparer.Ordinal);
            foreach (string file in files)
            {
                AssemblyDefinitionJson json = jsonByFile[file];

                string[] references = (json.references ?? Array.Empty<string>())
                    .Select(reference => ResolveReference(reference, guidToAssemblyName, json.name, file))
                    .Where(reference => !string.IsNullOrWhiteSpace(reference))
                    .ToArray();
                definitions.Add(json.name, new AssemblyDefinitionData(json.name, references));
            }

            return definitions;
        }

        private static void AssertGraphIsAcyclic(IReadOnlyDictionary<string, AssemblyDefinitionData> definitions)
        {
            HashSet<string> visited = new(StringComparer.Ordinal);
            HashSet<string> visiting = new(StringComparer.Ordinal);
            List<string> path = new();

            foreach (string assemblyName in definitions.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                Visit(assemblyName, definitions, visited, visiting, path);
            }
        }

        private static void Visit(
            string assemblyName,
            IReadOnlyDictionary<string, AssemblyDefinitionData> definitions,
            ISet<string> visited,
            ISet<string> visiting,
            IList<string> path)
        {
            if (visited.Contains(assemblyName))
            {
                return;
            }

            if (!visiting.Add(assemblyName))
            {
                int cycleStart = path.IndexOf(assemblyName);
                IEnumerable<string> cycle = path.Skip(Math.Max(0, cycleStart)).Concat(new[] { assemblyName });
                Assert.Fail($"Project assembly dependency cycle: {string.Join(" -> ", cycle)}");
            }

            path.Add(assemblyName);
            if (definitions.TryGetValue(assemblyName, out AssemblyDefinitionData definition))
            {
                foreach (string dependency in definition.References.Where(definitions.ContainsKey))
                {
                    Visit(dependency, definitions, visited, visiting, path);
                }
            }

            path.RemoveAt(path.Count - 1);
            visiting.Remove(assemblyName);
            visited.Add(assemblyName);
        }

        private static IEnumerable<string> EnumerateLogicFiles()
        {
            string root = ToAbsolutePath(RuntimeRoot);
            if (!Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => HasDirectorySegment(path, "Domain") || HasDirectorySegment(path, "Application"));
        }

        private static bool HasDirectorySegment(string path, string segment)
        {
            string marker = $"{Path.DirectorySeparatorChar}{segment}{Path.DirectorySeparatorChar}";
            return path.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ResolveReference(
            string reference,
            IReadOnlyDictionary<string, string> guidToAssemblyName,
            string sourceAssemblyName,
            string sourceFile)
        {
            const string guidPrefix = "GUID:";
            if (string.IsNullOrWhiteSpace(reference)) return string.Empty;
            if (!reference.StartsWith(guidPrefix, StringComparison.Ordinal))
            {
                // External package references are intentionally left in the
                // graph as opaque names. Project references must still be
                // known; silently accepting a misspelled VampireHunt name
                // would make the policy fail open.
                if (reference.StartsWith("VampireHunt.", StringComparison.Ordinal) &&
                    !AllowedDependencies.ContainsKey(reference))
                {
                    Assert.Fail(
                        $"{sourceAssemblyName} references unknown project assembly '{reference}' " +
                        $"from {ToAssetPath(sourceFile)}.");
                }

                return reference;
            }

            string guid = reference.Substring(guidPrefix.Length);
            if (!guidToAssemblyName.TryGetValue(guid, out string assemblyName))
            {
                Assert.Fail(
                    $"{sourceAssemblyName} references unresolved asmdef GUID '{guid}' " +
                    $"from {ToAssetPath(sourceFile)}.");
            }

            return assemblyName;
        }

        private static HashSet<string> Set(params string[] values) =>
            new(values, StringComparer.Ordinal);

        private static string GetHierarchyPath(Transform transform)
        {
            List<string> names = new();
            for (Transform current = transform; current != null; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unity project root cannot be resolved.");
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static string ToAssetPath(string absolutePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(absolutePath);
            string relative = normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(normalizedRoot.Length)
                : normalizedPath;
            return relative.Replace('\\', '/');
        }

        [Serializable]
        private sealed class AssemblyDefinitionJson
        {
            public string name = string.Empty;
            public string[] references = Array.Empty<string>();
        }

        private sealed class AssemblyDefinitionData
        {
            public string Name { get; }
            public IReadOnlyList<string> References { get; }

            public AssemblyDefinitionData(string name, IReadOnlyList<string> references)
            {
                Name = name;
                References = references;
            }
        }
    }
}
