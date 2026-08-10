using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Scarecrow
{
    public class SimpleShaderGUI : ShaderGUI
    {
        //折叠页缩进等级
        public const int FoldoutIndent = 1;
        public const string FoldoutSign = "_Foldout";



        public int FoldoutLevel { get { return _foldoutLevel; } }

        public int FoldoutLevel_Editor { get { return _foldoutLevel_Editor; } }

        public bool FoldoutOpen { get { return _foldoutOpen; } }

        public bool FoldoutEditor { get { return _foldoutEditor; } }

        public List<string> SwitchList = new List<string>();



        //PropertyGUI绘制在那级折叠页中
        private int _foldoutLevel = 0;

        private int _foldoutLevel_Editor = 0;

        private bool _foldoutOpen = true;

        private bool _foldoutEditor = true;

        private MaterialProperty[] _allProperties;



        private MaterialProperty _SrcBlend;
        private MaterialProperty _DstBlend;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            InitializationData();
            _allProperties = properties;

            _SrcBlend = FindProperty("_SrcBlend");
            _DstBlend = FindProperty("_DstBlend");
            TransparentSwitchButtonDraw();


            for (int i = 0; i < properties.Length; i++)
            {

                if (!IsFoldout(properties[i]))
                    EditorGUI.BeginDisabledGroup(!_foldoutEditor);

                if (_foldoutOpen || IsFoldout(properties[i]))
                {
                    if (properties[i].propertyFlags != UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector)
                        materialEditor.ShaderProperty(properties[i], properties[i].displayName);
                }

                if (!IsFoldout(properties[i]))
                    EditorGUI.EndDisabledGroup();
            }

            if (_foldoutOpen)
            {
                EditorGUI.BeginDisabledGroup(!_foldoutEditor);
                materialEditor.DoubleSidedGIField();
                materialEditor.RenderQueueField();
                EditorGUI.EndDisabledGroup();
            }
        }


        //折叠页设置，

        public void SetFoldout(int foldoutLevel, int foldoutLevel_Editor, bool foldoutState, bool foldoutEditor = true)
        {
            EditorGUI.indentLevel += (foldoutLevel - _foldoutLevel) * FoldoutIndent;
            _foldoutLevel = foldoutLevel;
            _foldoutLevel_Editor = foldoutLevel_Editor;
            _foldoutOpen = foldoutState;
            _foldoutEditor = foldoutEditor;
        }
        public bool GetShowState(string[] showList)
        {
            foreach (string show in showList)
            {
                if (SwitchList.Contains(show))
                    return true;
            }

            return false;
        }

        public MaterialProperty FindProperty(string name)
        {
            return ShaderGUI.FindProperty(name, _allProperties, false);
        }
        public static bool IsFoldout(MaterialProperty property)
        {
            string pattern = FoldoutSign + @"\s*$";
            return Regex.IsMatch(property.displayName, pattern);
        }

        public static string GetFoldoutDisplayName(MaterialProperty property)
        {
            string pattern = FoldoutSign + @"\s*$";
            Regex reg = new Regex(pattern);
            return reg.Replace(property.displayName, "");
        }


        private void InitializationData()
        {
            _foldoutLevel = 0;
            _foldoutLevel_Editor = 0;
            _foldoutOpen = true;
            _foldoutEditor = true;
            EditorGUI.indentLevel = 0;
            SwitchList.Clear();
        }

        private void TransparentSwitchButtonDraw()
        {
            if (_SrcBlend == null || _DstBlend == null)
                return;
            if (_SrcBlend.propertyType != UnityEngine.Rendering.ShaderPropertyType.Float || _DstBlend.propertyType != UnityEngine.Rendering.ShaderPropertyType.Float)
                return;

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("设置为不透明"))
            {
                _SrcBlend.floatValue = 1;
                _DstBlend.floatValue = 0;
                foreach (Material m in _SrcBlend.targets)
                {
                    m.renderQueue = 2000;
                }
            }
            if (GUILayout.Button("设置为半透明"))
            {
                _SrcBlend.floatValue = 5;
                _DstBlend.floatValue = 10;
                foreach (Material m in _SrcBlend.targets)
                {
                    m.renderQueue = 3000;
                }
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
