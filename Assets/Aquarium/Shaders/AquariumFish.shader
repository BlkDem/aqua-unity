// Fish bodies and fins, drawn instanced (see AquariumFishPasses.hlsl). Fins use the _FINS
// keyword: the fin atlas ripples, and they are transparent and two-sided. Pectorals (uv2) paddle
// in either mesh.
Shader "Aquarium/Fish"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 0.7
        _Roughness("Roughness", Range(0, 1)) = 0.3
        _Metallic("Metallic", Range(0, 1)) = 0.05
        [HDR] _EmissionColor("Emission", Color) = (0, 0, 0, 1)
        _CausticStrength("Caustics", Range(0, 2)) = 0.55
        _Glow("Rim glow (night creatures)", Float) = 0
        _Opacity("Opacity", Range(0, 1)) = 1
        [Toggle(_FINS)] _Fins("Fins", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        [Toggle] _ZWrite("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FishVertex
            #pragma fragment FishFragment
            #pragma shader_feature_local _FINS
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #define AQ_FORWARD
            #include "AquariumFishPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FishShadowVertex
            #pragma fragment FishShadowFragment
            #pragma shader_feature_local _FINS
            #pragma multi_compile_instancing
            #define AQ_SHADOW
            #include "AquariumFishPasses.hlsl"
            ENDHLSL
        }
    }
}
