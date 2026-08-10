using UnityEditor;
using UnityEngine;



namespace Scarecrow
{
 
    public enum FoldoutStyle
    {
        Big = 1,
        Median = 2,
        Small = 3
    }


    public class FoldoutDrawer : MaterialPropertyDrawer
    {

        private int _foldoutLevel = 1;

        private float _foldoutIndent = 15;

        private bool _foldoutOpen = true;

        private FoldoutStyle _foldoutStyle = FoldoutStyle.Big;

        private bool _foldoutToggleDraw = false;

        private bool _foldoutEditor = true;

        private SimpleShaderGUI _simpleShaderGUI;
  
        private MaterialProperty _property;

        private string[] _showList = new string[0];

        private bool _isAlwaysShow = true;


        public FoldoutDrawer() : this(1) { }
        public FoldoutDrawer(float foldoutLevel) : this(foldoutLevel, 1) { }
        public FoldoutDrawer(float foldoutLevel, float foldoutStyle) : this(foldoutLevel, foldoutStyle, 0) { }
        public FoldoutDrawer(float foldoutLevel, float foldoutStyle, float foldoutToggleDraw) : this(foldoutLevel, foldoutStyle, foldoutToggleDraw, 1) { }
        public FoldoutDrawer(float foldoutLevel, float foldoutStyle, float foldoutToggleDraw, float foldoutOpen, params string[] showList)
        {
            int level = (int)foldoutLevel;
            int style = (int)foldoutStyle;
            int toggleDraw = (int)foldoutToggleDraw;
            int open = (int)foldoutOpen;

            _foldoutLevel = level < 1 ? 1 : level;

            switch (style)
            {
                case 2: _foldoutStyle = FoldoutStyle.Median; break;
                case 3: _foldoutStyle = FoldoutStyle.Small; toggleDraw = 0; break;
                default: _foldoutStyle = FoldoutStyle.Big; break;
            }

            _foldoutToggleDraw = toggleDraw == 0 ? false : true;

            _foldoutOpen = open == 0 ? false : true;

            _showList = showList;
            _isAlwaysShow = showList == null || showList.Length == 0;
        }
        public override void Apply(MaterialProperty prop)
        {
            base.Apply(prop);
            //设置初始KeyWorld
            if (prop.propertyType == UnityEngine.Rendering.ShaderPropertyType.Float)
            {
                bool foldoutEditor = prop.floatValue != 0 ? true : false;
                SetFoldoutEditorKeyword(prop, foldoutEditor);
            }
        }

        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return -2;
        }
        public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
        {

            _simpleShaderGUI = editor.customShaderGUI as SimpleShaderGUI;
            if (_simpleShaderGUI == null)
            {
                GUILayout.Label(prop.displayName + " :   Please use SimpleShaderGUI in your shader");
                return;
            }
            if (prop.propertyType != UnityEngine.Rendering.ShaderPropertyType.Float)
            {
                GUILayout.Label(prop.displayName + " :   Property must be of type float");
                return;
            }
            if (!SimpleShaderGUI.IsFoldout(prop))
            {
                GUILayout.Label(prop.displayName + " :   Please add " + SimpleShaderGUI.FoldoutSign + " after displayName");
                return;
            }



            if (_foldoutLevel > _simpleShaderGUI.FoldoutLevel && !_simpleShaderGUI.FoldoutOpen)
                return;

            if (!(_isAlwaysShow || _simpleShaderGUI.GetShowState(_showList)))
            {
                _simpleShaderGUI.SetFoldout(_foldoutLevel, _simpleShaderGUI.FoldoutLevel_Editor, false, _simpleShaderGUI.FoldoutEditor);
                return;
            }

   
            _foldoutEditor = prop.floatValue != 0 ? true : false;
            _property = prop;

            FoldoutGUIDraw();


            int actual_foldoutEditorLevel = _simpleShaderGUI.FoldoutLevel_Editor;
            bool actual_foldoutEditor = _simpleShaderGUI.FoldoutEditor;

  
            bool state1 = _simpleShaderGUI.FoldoutEditor && !_foldoutEditor;

            bool state2 = !_simpleShaderGUI.FoldoutEditor && _foldoutEditor && _foldoutLevel <= _simpleShaderGUI.FoldoutLevel_Editor;
            if (state1 || state2)
            {
                actual_foldoutEditorLevel = _foldoutLevel;
                actual_foldoutEditor = _foldoutEditor;
            }
  
            _simpleShaderGUI.SetFoldout(_foldoutLevel, actual_foldoutEditorLevel, _foldoutOpen, actual_foldoutEditor);

            //当更改属性名时，unity不会调用Apply函数进行初始化(只会调用构造函数), 这里每次渲染时都来设置关键字,这或许是unity的bug?...因为在官方的[Toggle]也会出现这种问题(当修改属性名时不会初始化构造函数)
            //即便如此，OnGUI函数只有在绘制时才会被调用，所以如果该属性不会被绘制时(被折叠或不显示)，同时在shader里修改他的名字,同样不会设置关键字，一般不会有这种操作
            if (!_property.hasMixedValue)
                SetFoldoutEditorKeyword(_property, _foldoutEditor);
        }

        private void FoldoutGUIDraw()
        {
            switch (_foldoutStyle)
            {
                case FoldoutStyle.Big: FoldoutGUIDraw_Shuriken(30, 3); break;
                case FoldoutStyle.Median: FoldoutGUIDraw_Shuriken(25, 2); break;
                case FoldoutStyle.Small: FoldoutGUIDraw_Small(); break;
            }
        }


        private void FoldoutGUIDraw_Shuriken(float height, int fontSize)
        {

            if (_foldoutLevel > _simpleShaderGUI.FoldoutLevel_Editor && !_simpleShaderGUI.FoldoutEditor)
                EditorGUI.BeginDisabledGroup(true);

            GUIStyle style = new GUIStyle("ShurikenModuleTitle");
            style.border = new RectOffset(15, 7, 4, 4);
            style.font = EditorStyles.boldLabel.font;
            style.fontStyle = EditorStyles.boldLabel.fontStyle;
            style.fontSize = EditorStyles.boldLabel.fontSize + fontSize;
            style.fixedHeight = height;
            style.contentOffset = new Vector2(20f, -1);
            if (_foldoutToggleDraw)
                style.contentOffset += new Vector2(18f, 0); 

            Rect rect = GUILayoutUtility.GetRect(0, height, style);
            rect.xMin += (_foldoutLevel - 1) * _foldoutIndent;
            GUI.Box(rect, SimpleShaderGUI.GetFoldoutDisplayName(_property), style);

            Rect triangleRect = new Rect(rect.x + 4, rect.y + rect.height / 2 - 7, 14f, 14f);
            Event e = Event.current;

            if (e.type == EventType.Repaint)
                EditorStyles.foldout.Draw(triangleRect, false, false, _foldoutOpen, false);

            Rect toggleRect = new Rect(triangleRect.x + 16, triangleRect.y - 1, 14f, 14f);
            if (_foldoutToggleDraw)
            {
                EditorGUI.BeginChangeCheck();
                if (_property.hasMixedValue)
                    _foldoutEditor = GUI.Toggle(toggleRect, false, "", new GUIStyle("ToggleMixed"));
                else
                    _foldoutEditor = GUI.Toggle(toggleRect, _foldoutEditor, "");
                if (EditorGUI.EndChangeCheck())
                {
                    _property.floatValue = _foldoutEditor ? 1 : 0;

                    SetFoldoutEditorKeyword(_property, _foldoutEditor);
                }
            }

            EditorGUI.EndDisabledGroup();


            if (e.type == EventType.MouseDown)
            {

                if (rect.Contains(e.mousePosition) && !(_foldoutToggleDraw && toggleRect.Contains(e.mousePosition)))
                {
                    _foldoutOpen = !_foldoutOpen;
                    e.Use();
                }
            }
        }


        private void FoldoutGUIDraw_Small()
        {
 
            if (_foldoutLevel > _simpleShaderGUI.FoldoutLevel_Editor && !_simpleShaderGUI.FoldoutEditor)
                EditorGUI.BeginDisabledGroup(true);

            Rect rect = GUILayoutUtility.GetRect(0, 25, EditorStyles.foldout);
            rect.xMin += (_foldoutLevel - 1) * _foldoutIndent;
            Event e = Event.current;
            if (e.type == EventType.Repaint)
                EditorStyles.foldout.Draw(rect, SimpleShaderGUI.GetFoldoutDisplayName(_property), false, false, _foldoutOpen, false);

            EditorGUI.EndDisabledGroup();

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                _foldoutOpen = !_foldoutOpen;
                e.Use();
            }
        }


        private void SetFoldoutEditorKeyword(MaterialProperty pro, bool foldoutEditor)
        {

            string keyword = pro.name.ToUpperInvariant() + "_ON";
            foreach (Material m in pro.targets)
            {
                if (foldoutEditor)
                    m.EnableKeyword(keyword);
                else
                    m.DisableKeyword(keyword);
            }
        }
    }


    public class Foldout_Out : MaterialPropertyDrawer
    {

        private int _foldoutLevel = 1;

        private SimpleShaderGUI _simpleShaderGUI;

        public Foldout_Out() : this(1) { }
        public Foldout_Out(float foldoutLevel)
        {
            int level = (int)foldoutLevel - 1;
            _foldoutLevel = level < 0 ? 0 : level;
        }


        public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
        {
            return -2;
        }
        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {

            _simpleShaderGUI = editor.customShaderGUI as SimpleShaderGUI;
            if (_simpleShaderGUI == null)
            {
                GUILayout.Label(prop.displayName + " :   Please use SimpleShaderGUI in your shader");
                return;
            }
            if (!SimpleShaderGUI.IsFoldout(prop))
            {
                GUILayout.Label(prop.displayName + " :   Please add " + SimpleShaderGUI.FoldoutSign + " after displayName");
                return;
            }

            if (_foldoutLevel >= _simpleShaderGUI.FoldoutLevel && !_simpleShaderGUI.FoldoutOpen)
                return;


            int actual_foldoutEditorLevel = _simpleShaderGUI.FoldoutLevel_Editor;
            bool actual_foldoutEditor = _simpleShaderGUI.FoldoutEditor;

            bool state2 = !_simpleShaderGUI.FoldoutEditor && _foldoutLevel < _simpleShaderGUI.FoldoutLevel_Editor;
            if (state2)
            {
                actual_foldoutEditorLevel = _foldoutLevel;
                actual_foldoutEditor = true;
            }
            _simpleShaderGUI.SetFoldout(_foldoutLevel, actual_foldoutEditorLevel, true, actual_foldoutEditor);
        }
    }
}
