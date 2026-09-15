// Panda VFX 2.3 - Universal Render Pipeline implementation (Unity 6 / URP 17).
// The Shader name and every legacy property name are preserved so existing
// materials, particle custom data streams, VAT assets and the custom GUI remain compatible.
Shader "VFX/Pandavfx_v2.3"
{
    Properties
    {
        _VATFrameFactor("VATFrameFactor", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cullmode("Cullmode", Float) = 0
        [Toggle] _NormalTexCV("NormalTexCV", Float) = 0
        [Toggle] _NormalTexC("NormalTexC", Float) = 0
        _NormalScale("NormalScale", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _Ztest("Ztest", Float) = 4
        _VATTime("VATTime", Float) = 0
        _LightScale("LightScale", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _Scr("Scr", Float) = 5
        _TexCenter("TexCenter", Vector) = (0,0,0,0)
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst("Dst", Float) = 10
        [KeywordEnum(Normal,Polar,Cylinder,UV2)] _MainTexUVS("MainTexUVS", Float) = 0
        [KeywordEnum(X,Y,Z)] _Face("Face", Float) = 1
        [HideInInspector] _AddTex_ST("AddTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _MainTex_ST("MainTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _NormalTex_ST("NormalTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _VTOTex_ST("VTOTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _DistortTex_ST("DistortTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _DistortMaskTex_ST("DistortMaskTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _DissloveTexPlus_ST("DissloveTexPlus_ST", Vector) = (1,1,0,0)
        [HideInInspector] _DissloveTex_ST("DissloveTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _VTOMaskTex_ST("VTOMaskTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _MaskPlusTex_ST("MaskPlusTex_ST", Vector) = (1,1,0,0)
        [HideInInspector] _MaskTex_ST("MaskTex_ST", Vector) = (1,1,0,0)
        _MainAlpha("MainAlpha", Range(0, 100)) = 1
        [HDR] _MainColor("MainColor", Color) = (1,1,1,1)
        _AddTexUspeed("AddTexUspeed", Float) = 0
        _AddTexAC("AddTexAC", Range(0, 0.1)) = 0
        _MainTex_Uspeed("MainTex_Uspeed", Float) = 0
        _NormalTex_Uspeed("NormalTex_Uspeed", Float) = 0
        _AddTexVspeed("AddTexVspeed", Float) = 0
        _MainTex_Vspeed("MainTex_Vspeed", Float) = 0
        _NormalTex_Vspeed("NormalTex_Vspeed", Float) = 0
        [Toggle] _DistortAddTex("DistortAddTex", Float) = 1
        [Enum(off,0,on,1)] _DistortNormalTex("DistortNormalTex", Float) = 1
        [Enum(off,0,on,1)] _DistortMainTex("DistortMainTex", Float) = 1
        [Enum(off,0,on,1)] _DistortMask("DistortMask", Float) = 0
        [Enum(off,0,on,1)] _IfDissolveOffsetC("IfDissolveOffsetC", Float) = 0
        [Enum(off,0,on,1)] _DistortDisTex("DistortDisTex", Float) = 0
        _DistortFactor("DistortFactor", Range(0, 1)) = 0
        _DistortTex_Uspeed("DistortTex_Uspeed", Float) = 0
        _DistortTex_Vspeed("DistortTex_Vspeed", Float) = 0
        _DIssloveFactor("DIssloveFactor", Range(0, 1)) = 0.5
        _DIssloveWide("DIssloveWide", Range(0, 1)) = 0.1
        _DIssloveSoft("DIssloveSoft", Range(0, 1)) = 0.5
        [HDR] _DIssloveColor("DIssloveColor", Color) = (1,1,1,1)
        _DisTex_Uspeed("DisTex_Uspeed", Float) = 0
        _DisTex_Vspeed("DisTex_Vspeed", Float) = 0
        _VTOFactor("VTOFactor", Float) = 0
        _VTOTex_Uspeed("VTOTex_Uspeed", Float) = 0
        _VTOTex_Vspeed("VTOTex_Vspeed", Float) = 0
        _VTOMaskTex("VTOMaskTex", 2D) = "white" {}
        _fnl_power("fnl_power", Range(1, 10)) = 1
        _fnl_sacle("fnl_sacle", Range(0, 1)) = 0
        [HDR] _fnl_color("fnl_color", Color) = (1,1,1,0)
        _softFacotr("softFacotr", Range(0, 20)) = 1
        _DepthfadeFactor("DepthfadeFactor", Range(0, 10)) = 1
        [Toggle] _CustomdataMaskUV("CustomdataMaskUV", Float) = 0
        _Mask_scale("Mask_scale", Float) = 1
        _NormalTex_Rotat("NormalTex_Rotat", Range(0, 360)) = 0
        _MainTex_rotat("MainTex_rotat", Range(0, 360)) = 0
        _MainTexAC("MainTexAC", Range(0, 0.1)) = 0
        _AddTexRo("AddTexRo", Range(0, 360)) = 0
        _VTOR("VTOR", Range(0, 360)) = 0
        _VTOMaskR("VTOMaskR", Range(0, 360)) = 0
        _DissolvePlusR("DissolvePlusR", Range(0, 360)) = 0
        _DIssolve_rotat("DIssolve_rotat", Range(0, 360)) = 0
        [Toggle] _FNLfanxiangkaiguan("FNLfanxiangkaiguan", Float) = 0
        [Toggle] _ToggleSwitch0("Toggle Switch0", Float) = 0
        [Toggle] _Depthfadeon("Depthfadeon", Float) = 0
        _Mask_Uspeed("Mask_Uspeed", Float) = 0
        _MaskPlusUspeed("MaskPlusUspeed", Float) = 0
        _Mask_rotat("Mask_rotat", Range(0, 360)) = 0
        _MaskPlusR("MaskPlusR", Range(0, 360)) = 0
        _MaskPlusVspeed("MaskPlusVspeed", Float) = 0
        _Mask_Vspeed("Mask_Vspeed", Float) = 0
        [Toggle] _soft_sting("soft_sting", Float) = 0
        [Toggle] _VTOMaskAR("VTOMaskAR", Float) = 1
        [Toggle] _VTOMaskCV("VTOMaskCV", Float) = 0
        [Toggle] _VTOMaskC("VTOMaskC", Float) = 0
        _qubaohedu("qubaohedu", Range(0, 1)) = 0
        [HDR] _DepthColor("DepthColor", Color) = (0,0,0,0)
        _Zwrite("Zwrite", Float) = 0
        [Enum(Option1,0,Option2,1)] _DepthF("DepthF", Float) = 0
        _Dir("Dir", Vector) = (0,0,0,0)
        [HDR] _BackFaceColor("BackFaceColor", Color) = (1,1,1,0)
        [Toggle] _IfMaskColor("IfMaskColor", Float) = 0
        [Enum(Option1,0,Option2,1)] _CustomDistort("CustomDistort", Float) = 0
        [HDR] _AddTexColor("AddTexColor", Color) = (0,0,0,0)
        _AddTexBlend("AddTexBlend", Range(0, 1)) = 0
        [Toggle] _IfAddTex("IfAddTex", Float) = 0
        _DissolveTexDivide("DissolveTexDivide", Range(1, 10)) = 1
        [Enum(Option1,0,Option2,1)] _CustomdataDisT("CustomdataDisT", Float) = 0
        _DissloveTexPlus("DissloveTexPlus", 2D) = "black" {}
        _CenterU("CenterU", Float) = 0.5
        _CenterV("CenterV", Float) = 0.5
        [Enum(off,0,on,1)] _ScreenAsMain("ScreenAsMain", Float) = 0
        [Toggle] _softback("softback", Float) = 0
        _MainTexRefine("MainTexRefine", Vector) = (1,1,2,0)
        _AddTexRefine("AddTexRefine", Vector) = (1,1,2,0)
        [HideInInspector] _texcoord2("", 2D) = "white" {}
        [HideInInspector] _texcoord3("", 2D) = "white" {}
        [HideInInspector] _texcoord4("", 2D) = "white" {}
        [HideInInspector] _texcoord("", 2D) = "white" {}
        _VTOTexExp("VTOTexExp", Range(0, 10)) = 1
        _DissolveTexExp("DissolveTexExp", Range(0, 10)) = 1
        [Toggle] _IfCustomLight("IfCustomLight", Float) = 0
        _AddTex("AddTex", 2D) = "white" {}
        _DissloveTex("DissloveTex", 2D) = "white" {}
        _VTOTex("VTOTex", 2D) = "white" {}
        _MainTex("MainTex", 2D) = "white" {}
        _MaskTex("MaskTex", 2D) = "white" {}
        _MaskPlusTex("MaskPlusTex", 2D) = "white" {}
        [Toggle] _AddTexC("AddTexC", Float) = 0
        [Toggle] _MaintexC("MaintexC", Float) = 0
        [Toggle] _VTOC("VTOC", Float) = 0
        [Toggle] _MaskPlusC("MaskPlusC", Float) = 0
        [Toggle] _MaskC("MaskC", Float) = 0
        [Toggle] _DissolveC("DissolveC", Float) = 0
        [Toggle] _MaintexCV("MaintexCV", Float) = 0
        [Toggle] _VTOCV("VTOCV", Float) = 0
        [Toggle] _MaskPlusCV("MaskPlusCV", Float) = 0
        [Toggle] _MaskCV("MaskCV", Float) = 0
        [Toggle] _DissolvePlusC("DissolvePlusC", Float) = 0
        [Toggle] _DissolvePlusAR("DissolvePlusAR", Float) = 1
        [Toggle] _DissolvePlusCV("DissolvePlusCV", Float) = 0
        [Toggle] _DissolveCV("DissolveCV", Float) = 0
        [Toggle] _AddTexCV("AddTexCV", Float) = 0
        [Toggle] _MainTex_ar("MainTex_a/r", Float) = 0
        [Toggle] _VTOAR("VTOAR", Float) = 1
        [Toggle] _MaskPlusAR("MaskPlusAR", Float) = 1
        [Toggle] _MaskAlphaRA("MaskAlphaRA", Float) = 1
        [Toggle] _DissolveAR("DissolveAR", Float) = 1
        _NormalTex("NormalTex", 2D) = "white" {}
        _StaticNormalScale("StaticNormalScale", Float) = 0
        _StaticNormalOffset("StaticNormalOffset", Range(0, 1)) = 0
        [Toggle] _IfStaticNormal("IfStaticNormal", Float) = 0
        _CubemapScale("CubemapScale", Range(0, 10)) = 0
        [Toggle] _IfCubemap("IfCubemap", Float) = 0
        [Toggle] _IfDissolvePlus("IfDissolvePlus", Float) = 0
        _CubeMap("CubeMap", CUBE) = "white" {}
        [Toggle] _CustomdataDis("CustomdataDis", Float) = 0
        [Toggle] _sot_sting_A("sot_sting_A", Float) = 0
        [Enum(Alpha,0,Add,1,Multiply,2)] _AddTexBlendMode("AddTexBlendMode", Float) = 0
        _Parallax("Parallax", Range(0, 1)) = 0
        [Toggle] _IfPara("IfPara", Float) = 0
        _ParaTex("ParaTex", 2D) = "white" {}
        [Toggle] _IfFlowmap("IfFlowmap", Float) = 0
        [Toggle] _IfNormalDistort("IfNormalDistort", Float) = 0
        [Toggle] _DistortTexAR("DistortTexAR", Float) = 1
        _DistortTex("DistortTex", 2D) = "white" {}
        [Toggle] _DistortMaskTexAR("DistortMaskTexAR", Float) = 1
        _DistortMaskTex("DistortMaskTex", 2D) = "white" {}
        _DistortMaskTexR("DistortMaskTexR", Range(0, 360)) = 0
        [Toggle] _DistortMaskTexC("DistortMaskTexC", Float) = 0
        [Toggle] _DistortMaskTexCV("DistortMaskTexCV", Float) = 0
        [Toggle] _AddTexAR("AddTexAR", Float) = 0
        [Toggle] _IfAddTexAlpha("IfAddTexAlpha", Float) = 0
        [Toggle] _IfAddTexColor("IfAddTexColor", Float) = 1
        [Toggle] _AlphaAdd("AlphaAdd", Float) = 0
        [Toggle] _DistortRemap("DistortRemap", Float) = 0
        [Toggle] _VTORemap("VTORemap", Float) = 0
        [Enum(Normal,0,Polar,1,Cylinder,2,UV2,3)] _MaskTexUVS("MaskTexUVS", Float) = 0
        [Enum(Normal,0,Polar,1,Cylinder,2,UV2,3)] _DissolveTexUVS2("DissolveTexUVS2", Float) = 0
        [Enum(Normal,0,Polar,1,Cylinder,2,UV2,3)] _AddTexUVS("AddTexUVS", Float) = 0
        [Toggle] _CustomdataMainTexUV("CustomdataMainTexUV", Float) = 0
        [Toggle] _CAddTexUV("CAddTexUV", Float) = 0
        [Toggle] _CAddTexUVT("CAddTexUVT", Float) = 1
        _Reference("Reference", Range(0, 200)) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _Pass("Pass", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _Comparison("Comparison", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _Fail("Fail", Float) = 0
        _StencilStyle("StencilStyle", Float) = 0
        [Toggle] _IfMaskPlusTex("IfMaskPlusTex", Float) = 0
        [Toggle] _IfDissolveColor("IfDissolveColor", Float) = 1
        [Enum(Custom1,0,Custom2,1)] _DissolveFactorC1("DissolveFactorC1", Float) = 1
        [Enum(Custom1,0,Custom2,1)] _DistortFactorC1("DistortFactorC1", Float) = 1
        [Enum(Custom1,0,Custom2,1)] _MainOffsetVC1("MainOffsetVC1", Float) = 0
        [Enum(Custom1,0,Custom2,1)] _MaskOffsetVC1("MaskOffsetVC1", Float) = 0
        [Enum(Custom1,0,Custom2,1)] _MainOffsetUC1("MainOffsetUC1", Float) = 0
        [Enum(Custom1,0,Custom2,1)] _DissolveOffsetVC1("DissolveOffsetVC1", Float) = 1
        [Enum(Custom1,0,Custom2,1)] _MaskOffsetUC1("MaskOffsetUC1", Float) = 0
        [Enum(Custom1,0,Custom2,1)] _VATFrameC1("VATFrameC1", Float) = 1
        [Enum(Custom1,0,Custom2,1)] _VTOFactorC1("VTOFactorC1", Float) = 1
        [Enum(x,0,y,1,z,2,w,3,none,4)] _MainOffsetVC2("MainOffsetVC2", Float) = 1
        [Enum(x,0,y,1,z,2,w,3,none,4)] _MaskOffsetVC2("MaskOffsetVC2", Float) = 3
        [Enum(Custom1,0,Custom2,1)] _DissolveOffsetUC1("DissolveOffsetUC1", Float) = 1
        [Enum(x,0,y,1,z,2,w,3,none,4)] _DissolveOffsetVC2("DissolveOffsetVC2", Float) = 1
        [Enum(x,0,y,1,z,2,w,3,none,4)] _DissolveFactorC2("DissolveFactorC2", Float) = 2
        [Enum(x,0,y,1,z,2,w,3,none,4)] _DissolveOffsetUC2("DissolveOffsetUC2", Float) = 0
        [Enum(x,0,y,1,z,2,w,3,none,4)] _DistortFactorC2("DistortFactorC2", Float) = 3
        [Enum(x,0,y,1,z,2,w,3,none,4)] _MainOffsetUC2("MainOffsetUC2", Float) = 0
        [Enum(x,0,y,1,z,2,w,3,none,4)] _MaskOffsetUC2("MaskOffsetUC2", Float) = 2
        [Enum(x,0,y,1,z,2,w,3,none,4)] _VATFrameC2("VATFrameC2", Float) = 4
        [Enum(x,0,y,1,z,2,w,3,none,4)] _VTOFactorC2("VTOFactorC2", Float) = 4
        [Toggle] _IfFNLAlpha("IfFNLAlpha", Float) = 0
        _VATPositionTex("VATPositionTex", 2D) = "black" {}
        _VATNormalTex("VATNormalTex", 2D) = "black" {}
        [Toggle] _IfVAT("IfVAT", Float) = 0
        [Toggle] _CustomVAT("CustomVAT", Float) = 0
        [Toggle] _ParticleVAT("ParticleVAT", Float) = 0
        [Toggle] _screenVTOon("screenVTOon", Float) = 0
        [HideInInspector] __dirty("", Int) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+0"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
            "IgnoreProjector" = "True"
            "IsEmissive" = "true"
        }

        Cull [_Cullmode]
        ZWrite [_Zwrite]
        ZTest [_Ztest]
        Blend [_Scr] [_Dst]
        Stencil
        {
            Ref [_Reference]
            Comp [_Comparison]
            Pass [_Pass]
            Fail [_Fail]
        }

        Pass
        {
            Name "PandaVFXForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex PandaVfxVert
            #pragma fragment PandaVfxFrag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Pandavfx_v2.3_URP.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Blend One Zero
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex PandaVfxShadowVert
            #pragma fragment PandaVfxShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Pandavfx_v2.3_URP.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
    CustomEditor "PandavfxGUI23"
}
