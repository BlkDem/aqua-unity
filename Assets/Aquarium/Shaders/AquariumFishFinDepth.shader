// The solid part of a fin in the depth texture and nothing else, so that depth of field blurs a fin
// by where the fin is and not by what lies behind it (see FishModel.FinDepthMaterials).
Shader "Aquarium/Fish Fin Depth"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 1
        _FinClear("Membrane opacity, no pigment", Range(0, 1)) = 0.22
        _FinDense("Membrane opacity, full pigment", Range(0, 1)) = 0.78
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "DepthLast"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FishDepthVertex
            #pragma fragment FishDepthFragment
            #pragma multi_compile_local _ _FINS _MEMBRANE
            #pragma multi_compile_instancing
            #define AQ_DEPTH
            #include "AquariumFishPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FishDepthVertex
            #pragma fragment FishDepthFragment
            #pragma multi_compile_local _ _FINS _MEMBRANE
            #pragma multi_compile_instancing
            #define AQ_DEPTH
            #include "AquariumFishPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FishDepthVertex
            #pragma fragment FishDepthFragment
            #pragma multi_compile_local _ _FINS _MEMBRANE
            #pragma multi_compile_instancing
            #define AQ_DEPTH
            #define AQ_DEPTH_NORMALS
            #include "AquariumFishPasses.hlsl"
            ENDHLSL
        }
    }
}
