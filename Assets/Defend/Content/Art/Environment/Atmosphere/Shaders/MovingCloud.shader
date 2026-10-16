Shader "Defend/Environment/Moving Clouds"
{
    Properties
    {
        _BaseMap("写实积云颜色与透明度", 2D) = "white" {}
        _SunlitColor("日照云色", Color) = (1, .985, .955, 1)
        _ShadowColor("云内冷色", Color) = (.63, .72, .83, 1)
        _Density("云层厚度", Range(.1, 4)) = 1.2
        _Opacity("云层透明度", Range(0, 1)) = .97
        _WorldTextureSize("主云团周期（米）", Float) = 76
        _EdgeSoftness("边缘过渡范围", Range(.05, .45)) = .12
        [HideInInspector] _CloudFlowOffsets("会话驱动的双层风流相位", Vector) = (0, 0, .37, .63)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "MovingClouds"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _SunlitColor;
                half4 _ShadowColor;
                float4 _CloudFlowOffsets;
                float _WorldTextureSize;
                float _EdgeSoftness;
                half _Density;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldPosition : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.worldPosition = positionWS.xz / max(_WorldTextureSize, 1);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 平流云团颜色和透明度，不移动云网格；原始高度和甲板净空保持不变。
                float2 primaryUv = input.worldPosition - _CloudFlowOffsets.xy;
                float2 secondaryUv = input.worldPosition * 1.63 - _CloudFlowOffsets.zw;
                half4 primary = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, primaryUv);
                half4 secondary = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, secondaryUv);
                // 主层保留成团积云的真实明暗；次层仅补少量远处薄云，避免双层混成白雾。
                half secondaryAlpha = secondary.a * .18h * (1 - primary.a);
                half density = primary.a + secondaryAlpha;
                half3 cloudColor = (primary.rgb * primary.a + secondary.rgb * secondaryAlpha) /
                    max(density, .0001h);

                // 原网格边缘逐渐透明，不让世界空间采样显露出矩形卡片边界。
                float2 borderDistance = min(input.uv, 1 - input.uv);
                float2 borderFade = smoothstep(0, max(_EdgeSoftness, .01), borderDistance);
                half envelope = borderFade.x * borderFade.y;
                half opacity = (1 - pow(saturate(1 - density), _Density)) * envelope * _Opacity;

                // RGB中已经包含日照亮部与蓝灰自阴影，不再把整团云重着色为均匀灰白。
                half3 color = cloudColor * _SunlitColor.rgb;
                color += _ShadowColor.rgb * .025h * (1 - cloudColor);
                return half4(MixFog(color, input.fogFactor), opacity);
            }
            ENDHLSL
        }
    }
}
