Shader "Defend/BatteryChargeDisplay"
{
    Properties
    {
        _BaseMap("观察窗原始纹理", 2D) = "white" {}
        _BaseColor("充能区域颜色", Color) = (1,1,1,1)
        _EmptyColor("空仓暗玻璃颜色", Color) = (.22,.25,.28,1)
        _Metallic("金属度", Range(0,1)) = .1
        _Smoothness("光滑度", Range(0,1)) = .65
        _EnergyEmission("已储能区域发光强度", Range(0,5)) = 1.1
        [PerRendererData] _EnergyFraction("真实储能比例", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmptyColor;
            half _Metallic;
            half _Smoothness;
            half _EnergyEmission;
            float _EnergyFraction;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "BatteryCharge"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                // UV2.x 由模型提供：观察窗最低点为 0，最高点为 1。
                float2 fillCoordinates : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fillHeight : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                half3 vertexLighting : TEXCOORD6;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fillHeight = input.fillCoordinates.x;
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float fraction = saturate(_EnergyFraction);
                float height = saturate(input.fillHeight);
                float edgeWidth = max(fwidth(height), .001);
                // 端点单独处理，空电没有残留亮线，满电不会在顶部留暗边。
                half filled = fraction <= 0 ? 0 : fraction >= 1 ? 1
                    : smoothstep(height - edgeWidth, height + edgeWidth, fraction);
                half3 original = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half luminance = dot(original, half3(.2126,.7152,.0722));
                // 去除原蓝色，但保留观察窗原有污渍和刻痕；空仓仍由真实灯光和环境反射照亮。
                half glassDetail = lerp(.25h, 1.3h, sqrt(saturate(luminance)));
                half3 emptyGlass = _EmptyColor.rgb * glassDetail;
                half3 chargedGlass = original * _BaseColor.rgb;

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(emptyGlass, chargedGlass, filled);
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0,0,1);
                surface.emission = chargedGlass * (_EnergyEmission * filled);
                surface.occlusion = 1;
                surface.alpha = 1;

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = input.shadowCoord;
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.vertexLighting = input.vertexLighting;
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1,1,1,1);
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            // 观察窗始终为实体表面，阴影不随储能液位裁切。
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            { return TransformObjectToHClip(positionOS.xyz); }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
