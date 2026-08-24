using System;
using UnityEditor;
using UnityEngine;
using VampireHunt.Boss.Abilities;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Editor.Boss
{
    [CustomEditor(typeof(BossAbilityAsset))]
    public sealed class BossAbilityAssetEditor : UnityEditor.Editor
    {
        private SerializedProperty m_LogicScript;
        private SerializedProperty m_LogicTypeName;
        private SerializedProperty m_PresentationCues;

        private void OnEnable()
        {
            m_LogicScript = serializedObject.FindProperty("logicScript");
            m_LogicTypeName = serializedObject.FindProperty("logicTypeName");
            m_PresentationCues = serializedObject.FindProperty("presentationCues");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "logicScript",
                "logicTypeName",
                "presentationCues");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gameplay Logic", EditorStyles.boldLabel);

            MonoScript current = m_LogicScript.objectReferenceValue as MonoScript;
            EditorGUI.BeginChangeCheck();
            MonoScript selected = (MonoScript)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Logic",
                    "Select a .cs script that implements IBossAbilityLogicRuntime. The script stays under Scripts; this asset only stores its reference."),
                current,
                typeof(MonoScript),
                allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
            {
                m_LogicScript.objectReferenceValue = selected;
                m_LogicTypeName.stringValue = TryGetLogicType(selected, out Type logicType, out _)
                    ? logicType.AssemblyQualifiedName
                    : string.Empty;
            }

            if (!TryGetLogicType(selected, out Type selectedType, out string error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else
            {
                string expectedTypeName = selectedType.AssemblyQualifiedName;
                if (m_LogicTypeName.stringValue != expectedTypeName)
                    m_LogicTypeName.stringValue = expectedTypeName;
                EditorGUILayout.HelpBox(
                    $"Runtime logic: {selectedType.FullName}\nCode location: {AssetDatabase.GetAssetPath(selected)}",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(m_PresentationCues, includeChildren: true);
            serializedObject.ApplyModifiedProperties();
        }

        internal static bool TryGetLogicType(MonoScript script, out Type logicType, out string error)
        {
            logicType = null;
            if (script == null)
            {
                error = "Assign a Boss ability logic .cs script.";
                return false;
            }

            logicType = script.GetClass();
            if (logicType == null)
            {
                error = "The selected file does not contain a loadable class with the same name as the .cs file.";
                return false;
            }
            if (!typeof(IBossAbilityLogicRuntime).IsAssignableFrom(logicType))
            {
                error = $"{logicType.FullName} does not implement {nameof(IBossAbilityLogicRuntime)}.";
                return false;
            }
            if (logicType.IsAbstract || logicType.IsInterface)
            {
                error = "The selected logic script must be a concrete class.";
                return false;
            }
            if (logicType.GetConstructor(Type.EmptyTypes) == null)
            {
                error = "The selected logic script must have a public parameterless constructor.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
