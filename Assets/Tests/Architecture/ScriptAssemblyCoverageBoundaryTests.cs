using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

namespace VampireHunt.Tests.Architecture
{
    public sealed class ScriptAssemblyCoverageBoundaryTests
    {
        private const string ScriptsRoot = "Assets/Scripts";
        private const string StatusFxAssemblyRoot = "Assets/Art/VFX/Piloto Studio/Shaders_Reforged/StatusFX";
        private const string CompatibilityAssembly = "VampireHunt.Legacy";

        [Test]
        public void EveryScript_IsOwnedByTheNearestAssemblyDefinition()
        {
            string root = ToAbsolutePath(ScriptsRoot);
            string[] asmdefPaths = Directory.EnumerateFiles(root, "*.asmdef", SearchOption.AllDirectories)
                .ToArray();
            Assert.That(asmdefPaths, Is.Not.Empty, $"No asmdef files found below {ScriptsRoot}.");

            string[] uncovered = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => FindNearestAssemblyDirectory(path, asmdefPaths) == null)
                .Select(ToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                uncovered,
                Is.Empty,
                "Every Assets/Scripts/**/*.cs must be covered by its nearest asmdef: " +
                string.Join(", ", uncovered));

            string[] predefined = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(ToAssetPath)
                .Where(path =>
                {
                    string assembly = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
                    return string.IsNullOrWhiteSpace(assembly) ||
                           assembly.StartsWith("Assembly-CSharp", StringComparison.Ordinal);
                })
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                predefined,
                Is.Empty,
                "Unity must compile every Assets/Scripts/**/*.cs through an asmdef, not a predefined assembly: " +
                string.Join(", ", predefined));
        }

        [Test]
        public void ScriptAssemblies_AreClosedAcyclicAndKeepCompatibilityAtTheOuterBoundary()
        {
            Dictionary<string, AssemblyDefinitionData> definitions = LoadDefinitions();
            Assert.That(definitions, Does.ContainKey(CompatibilityAssembly));

            foreach (AssemblyDefinitionData definition in definitions.Values)
            {
                Assert.That(
                    definition.Source.IndexOf("Assembly-CSharp", StringComparison.Ordinal),
                    Is.LessThan(0),
                    $"{definition.Name} must not reference the predefined Assembly-CSharp assembly.");

                bool editorOnly = definition.IsEditorOnly;
                if (IsEditorDirectory(definition.Source))
                {
                    Assert.That(
                        editorOnly,
                        Is.True,
                        $"Editor script assembly must be Editor-only: {ToAssetPath(definition.Source)}");
                }

                if (!editorOnly)
                {
                    Assert.That(
                        definition.Source.IndexOf("UnityEditor", StringComparison.Ordinal),
                        Is.LessThan(0),
                        $"Runtime asmdef must not reference UnityEditor: {ToAssetPath(definition.Source)}");
                }

                foreach (string reference in definition.ProjectReferences)
                {
                    Assert.That(
                        definitions.ContainsKey(reference),
                        Is.True,
                        $"{definition.Name} references unknown project assembly {reference}.");

                    AssemblyDefinitionData dependency = definitions[reference];
                    Assert.That(
                        dependency.IsEditorOnly && !editorOnly,
                        Is.False,
                        $"Runtime assembly {definition.Name} cannot reference Editor-only assembly {reference}.");

                    if (!editorOnly && !string.Equals(definition.Name, CompatibilityAssembly, StringComparison.Ordinal))
                    {
                        Assert.That(
                            reference,
                            Is.Not.EqualTo(CompatibilityAssembly),
                            $"{definition.Name} cannot depend on the outer compatibility assembly {CompatibilityAssembly}.");
                    }
                }
            }

            Assert.That(
                definitions[CompatibilityAssembly].ProjectReferences,
                Does.Contain("VampireHunt.Bootstrap"),
                $"{CompatibilityAssembly} must remain the explicit outer compatibility/adapter assembly.");

            AssertGraphIsAcyclic(definitions);
        }

        private static Dictionary<string, AssemblyDefinitionData> LoadDefinitions()
        {
            string[] files = EnumerateAssemblyDefinitionFiles()
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Dictionary<string, string> guidToName = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, AssemblyDefinitionData> definitions = new(StringComparer.Ordinal);

            foreach (string file in files)
            {
                AssemblyDefinitionJson json = JsonUtility.FromJson<AssemblyDefinitionJson>(File.ReadAllText(file));
                Assert.That(json, Is.Not.Null, $"Invalid asmdef JSON: {ToAssetPath(file)}");
                Assert.That(json.name, Is.Not.Null.And.Not.Empty, $"Asmdef has no name: {ToAssetPath(file)}");
                Assert.That(
                    definitions.ContainsKey(json.name),
                    Is.False,
                    $"Duplicate project assembly name {json.name}: {ToAssetPath(file)}");

                string metaPath = file + ".meta";
                Assert.That(File.Exists(metaPath), Is.True, $"Asmdef is missing its .meta: {ToAssetPath(file)}");
                Match guidMatch = Regex.Match(File.ReadAllText(metaPath), @"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$");
                Assert.That(guidMatch.Success, Is.True, $"Asmdef has invalid .meta GUID: {ToAssetPath(metaPath)}");
                Assert.That(
                    guidToName.TryAdd(guidMatch.Groups[1].Value, json.name),
                    Is.True,
                    $"Duplicate asmdef GUID in {ToAssetPath(file)}.");

                definitions.Add(
                    json.name,
                    new AssemblyDefinitionData(
                        json.name,
                        file,
                        json.includePlatforms ?? Array.Empty<string>(),
                        json.references ?? Array.Empty<string>()));
            }

            foreach (AssemblyDefinitionData definition in definitions.Values)
            {
                foreach (string rawReference in definition.RawReferences)
                {
                    string reference = ResolveReference(rawReference, guidToName, definition);
                    if (reference.StartsWith("VampireHunt.", StringComparison.Ordinal) ||
                        definitions.ContainsKey(reference))
                    {
                        Assert.That(
                            definitions.ContainsKey(reference),
                            Is.True,
                            $"{definition.Name} references unknown project assembly {reference}.");
                        definition.ProjectReferences.Add(reference);
                    }
                }
            }

            return definitions;
        }

        private static string ResolveReference(
            string rawReference,
            IReadOnlyDictionary<string, string> guidToName,
            AssemblyDefinitionData source)
        {
            if (string.IsNullOrWhiteSpace(rawReference))
                return string.Empty;

            const string guidPrefix = "GUID:";
            if (rawReference.StartsWith(guidPrefix, StringComparison.Ordinal))
            {
                string guid = rawReference.Substring(guidPrefix.Length);
                Assert.That(
                    guidToName.TryGetValue(guid, out string resolved),
                    Is.True,
                    $"{source.Name} references unresolved asmdef GUID {guid}.");
                return resolved;
            }

            if (rawReference.StartsWith("VampireHunt.", StringComparison.Ordinal))
            {
                return rawReference;
            }

            Assert.That(
                string.Equals(rawReference, "Assembly-CSharp", StringComparison.Ordinal),
                Is.False,
                $"{source.Name} must not reference the predefined Assembly-CSharp assembly.");
            return rawReference;
        }

        private static void AssertGraphIsAcyclic(IReadOnlyDictionary<string, AssemblyDefinitionData> definitions)
        {
            HashSet<string> visited = new(StringComparer.Ordinal);
            HashSet<string> visiting = new(StringComparer.Ordinal);
            List<string> path = new();

            foreach (string name in definitions.Keys.OrderBy(name => name, StringComparer.Ordinal))
                Visit(name, definitions, visited, visiting, path);
        }

        private static void Visit(
            string name,
            IReadOnlyDictionary<string, AssemblyDefinitionData> definitions,
            ISet<string> visited,
            ISet<string> visiting,
            IList<string> path)
        {
            if (visited.Contains(name))
                return;

            Assert.That(visiting.Add(name), Is.True, $"Project assembly dependency cycle: {string.Join(" -> ", path)} -> {name}");
            path.Add(name);
            foreach (string dependency in definitions[name].ProjectReferences)
                Visit(dependency, definitions, visited, visiting, path);
            path.RemoveAt(path.Count - 1);
            visiting.Remove(name);
            visited.Add(name);
        }

        private static string FindNearestAssemblyDirectory(string scriptPath, IReadOnlyCollection<string> asmdefPaths)
        {
            return asmdefPaths
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrEmpty(path) && IsWithin(scriptPath, path))
                .OrderByDescending(path => path.Length)
                .FirstOrDefault();
        }

        private static bool IsWithin(string path, string directory)
        {
            string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedPath, normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEditorDirectory(string asmdefPath)
        {
            return ToAssetPath(asmdefPath).Split('/')
                .Any(segment => string.Equals(segment, "Editor", StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> EnumerateAssemblyDefinitionFiles()
        {
            foreach (string rootAssetPath in new[] { ScriptsRoot, StatusFxAssemblyRoot })
            {
                string root = ToAbsolutePath(rootAssetPath);
                if (!Directory.Exists(root))
                    continue;

                foreach (string path in Directory.EnumerateFiles(root, "*.asmdef", SearchOption.AllDirectories))
                    yield return path;
            }
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
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(absolutePath);
            return (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    ? path.Substring(root.Length)
                    : path)
                .Replace('\\', '/');
        }

        [Serializable]
        private sealed class AssemblyDefinitionJson
        {
            public string name = string.Empty;
            public string[] references = Array.Empty<string>();
            public string[] includePlatforms = Array.Empty<string>();
        }

        private sealed class AssemblyDefinitionData
        {
            public string Name { get; }
            public string Source { get; }
            public string[] IncludePlatforms { get; }
            public string[] RawReferences { get; }
            public List<string> ProjectReferences { get; } = new();
            public bool IsEditorOnly => IncludePlatforms.Any(platform => string.Equals(platform, "Editor", StringComparison.OrdinalIgnoreCase));

            public AssemblyDefinitionData(string name, string source, string[] includePlatforms, string[] rawReferences)
            {
                Name = name;
                Source = source;
                IncludePlatforms = includePlatforms;
                RawReferences = rawReferences;
            }
        }
    }
}
