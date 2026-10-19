// Import this variant only into a project with Universal Render Pipeline installed.
// API target: URP 14 / Unity 2022.3. HDRP is not included.
Shader "SkyCity/MonsterGate/Portal URP"
{
    Properties
    {
        [MainTexture] _MainTex ("Optional Portal Texture", 2D) = "white" {}
        [HDR][MainColor] _BaseColor ("Base Color", Color) = (0.08, 0.64, 1, 1)
        [HideInInspector] _Color ("Legacy Color Alias", Color) = (0.08, 0.64, 1, 1)
        [HDR] _EmissionColor ("Emission", Color) = (0.26, 2.05, 3.2, 1)
        _GateVisibility ("Visibility", Range(0, 1)) = 1
        _GatePhase ("Animation Phase (0..1)", Range(0, 1)) = 0
        _TextureInfluence ("Texture Influence", Range(0, 1)) = 0.35
        _SwirlTightness ("Swirl Tightness", Range(0, 8)) = 3.1
        _EdgeSoftness ("Edge Softness", Range(0.005, 0.5)) = 0.10
        _Opacity ("Opacity", Range(0, 1)) = 0.95
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            Name "PortalUnlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float4 _Color;
                float4 _EmissionColor;
                float _GateVisibility;
                float _GatePhase;
                float _TextureInfluence;
                float _SwirlTightness;
                float _EdgeSoftness;
                float _Opacity;
            CBUFFER_END

            #include "../Shared/MonsterGatePortalCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 textureUV : TEXCOORD1;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.textureUV = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                return GateEvaluate(input.uv, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.textureUV));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
