using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

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
                ["VampireHunt.Boss"] = Set("VampireHunt.Core", "VampireHunt.Stats", "VampireHunt.Combat", "VampireHunt.Abilities", "VampireHunt.Navigation")
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
                if (!AllowedDependencies.TryGetValue(assemblyName, out HashSet<string> allowed))
                {
                    continue;
                }

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

        private static Dictionary<string, AssemblyDefinitionData> LoadRuntimeAssemblyDefinitions()
        {
            string absoluteRoot = ToAbsolutePath(RuntimeRoot);
            if (!Directory.Exists(absoluteRoot))
            {
                return new Dictionary<string, AssemblyDefinitionData>(StringComparer.Ordinal);
            }

            Dictionary<string, AssemblyDefinitionData> definitions = new(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(absoluteRoot, "*.asmdef", SearchOption.AllDirectories))
            {
                AssemblyDefinitionJson json = JsonUtility.FromJson<AssemblyDefinitionJson>(File.ReadAllText(file));
                if (json == null || string.IsNullOrWhiteSpace(json.name))
                {
                    Assert.Fail($"Invalid asmdef JSON: {ToAssetPath(file)}");
                }

                string[] references = (json.references ?? Array.Empty<string>())
                    .Select(NormalizeReference)
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

        private static string NormalizeReference(string reference)
        {
            const string guidPrefix = "GUID:";
            return reference != null && reference.StartsWith(guidPrefix, StringComparison.Ordinal)
                ? reference
                : reference ?? string.Empty;
        }

        private static HashSet<string> Set(params string[] values) =>
            new(values, StringComparer.Ordinal);

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
