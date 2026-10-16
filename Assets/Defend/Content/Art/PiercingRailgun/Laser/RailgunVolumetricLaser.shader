// URP adaptation of the supplied SineVFX Volumetric 3D Lasers 1.2 additive layer.
// Original source and material settings are retained under Source/.
Shader "Defend/Effects/RailgunVolumetricLaser"
{
    Properties
    {
        [HDR] _FinalColor ("Laser color", Color) = (1, 0.24, 0.025, 1)
        _FinalPower ("Brightness", Float) = 3
        _LayerOpacity ("Volume opacity", Range(0, 1)) = 0.8
        _Noise01 ("Original noise", 2D) = "white" {}
        _Noise01Distortion ("Original noise distortion", 2D) = "gray" {}
        _OpacityCutoff ("Original dissolve texture", 2D) = "white" {}
        _Noise01ScrollSpeed ("Noise scroll speed", Float) = -18
        _Noise01DistortionScrollSpeed ("Distortion scroll speed", Float) = -9
        _Noise01DistortionPower ("Distortion amount", Range(0, 1)) = 0.1
        _VertexOffsetPower ("Surface displacement", Range(0, 0.08)) = 0.015
        _FinalRimExp ("Edge softness", Range(0, 4)) = 0.65
        _Progress ("Envelope", Range(0, 1)) = 1
        _EffectSeconds ("Simulation elapsed time", Float) = 0
        _Distance ("Authoritative ray length", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "RailgunPulse"
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Local premultiplied compositing keeps the warm hue on cameras without tone mapping.
            Blend One OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Noise01); SAMPLER(sampler_Noise01);
            TEXTURE2D(_Noise01Distortion); SAMPLER(sampler_Noise01Distortion);
            TEXTURE2D(_OpacityCutoff); SAMPLER(sampler_OpacityCutoff);
            CBUFFER_START(UnityPerMaterial)
                float4 _FinalColor;
                float4 _Noise01_ST;
                float4 _Noise01Distortion_ST;
                float4 _OpacityCutoff_ST;
                float _FinalPower;
                float _LayerOpacity;
                float _Noise01ScrollSpeed;
                float _Noise01DistortionScrollSpeed;
                float _Noise01DistortionPower;
                float _VertexOffsetPower;
                float _FinalRimExp;
                float _Progress;
                float _EffectSeconds;
                float _Distance;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 laserUV : TEXCOORD2;
                float along : TEXCOORD3;
                float fog : TEXCOORD4;
            };
            float2 DistortedNoiseUV(float2 uv)
            {
                float2 distortionUV = (uv + float2(_EffectSeconds * _Noise01DistortionScrollSpeed, 0)) * _Noise01Distortion_ST.xy + _Noise01Distortion_ST.zw;
                float distortion = SAMPLE_TEXTURE2D_LOD(_Noise01Distortion, sampler_Noise01Distortion, distortionUV, 0).r * 2 - 1;
                return (uv + float2(_EffectSeconds * _Noise01ScrollSpeed, 0) + distortion * _Noise01DistortionPower) * _Noise01_ST.xy + _Noise01_ST.zw;
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Authoring normalizes the supplied cylinder to local z = 0..1.
                float along = saturate(input.positionOS.z);
                float2 uv = float2(along * _Distance, input.uv.y);
                float noise = SAMPLE_TEXTURE2D_LOD(_Noise01, sampler_Noise01, DistortedNoiseUV(uv), 0).r;
                float ends = saturate(along * _Distance * 18) * saturate((1 - along) * _Distance * 18);
                input.positionOS.xy += input.normalOS.xy * (noise - 0.5) * _VertexOffsetPower * ends * _Progress;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.laserUV = uv;
                output.along = along;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(face, 1, -1);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float noise = SAMPLE_TEXTURE2D(_Noise01, sampler_Noise01, DistortedNoiseUV(input.laserUV)).r;
                float opacity = SAMPLE_TEXTURE2D(_OpacityCutoff, sampler_OpacityCutoff, input.laserUV * _OpacityCutoff_ST.xy + _OpacityCutoff_ST.zw).r;
                float dissolve = smoothstep(0.42, 0.65, opacity * 0.35 + saturate(_Progress));
                float ends = saturate(input.along * _Distance * 18) * saturate((1 - input.along) * _Distance * 18);
                // A cylindrical volume is brightest through its centre and fades at its silhouette.
                // The supplied outward-rim treatment made the enlarged halo look like a solid strip.
                float volume = pow(saturate(abs(dot(normal, view))), _FinalRimExp);
                float alpha = saturate(_LayerOpacity * volume * ends * dissolve * _Progress);
                half3 emission = max(half3(0, 0, 0), _FinalColor.rgb) * _FinalPower * lerp(0.8, 1.08, noise);
                // Normalize by the brightest channel, preserving hue without per-channel HDR clipping.
                emission /= max(1.0h, max(emission.r, max(emission.g, emission.b)));
                emission = MixFogColor(emission, half3(0, 0, 0), input.fog);
                return half4(emission * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
