Shader "Custom/SimpleNPR"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)

        [Range(0, 1)]
        _Metallic ("Metallic", Range(0, 1)) = 0.0

        [Range(0, 1)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5

        [HideInInspector]
        _WhiteOverlay ("WhiteOverlay", Range(0, 1)) = 0//闪白
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM

            #pragma target 3.5

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fog

            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)

                half4 _Color;
                half _Metallic;
                half _Smoothness;
                half _WhiteOverlay;

            CBUFFER_END



            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS    : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);

                output.positionCS =
                    positionInputs.positionCS;

                output.positionWS =
                    positionInputs.positionWS;

                output.normalWS =
                    normalInputs.normalWS;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // cos(N, L) * 阴影衰减的切分阈值。
                const half kLambertThreshold = 0.5h;

                // 暗部映射值。
                const half kShadowBandValue = 0.25h;

                // 亮部映射值。
                const half kLightBandValue = 1.0h;


                half3 normalWS =
                    normalize(input.normalWS);

                float4 shadowCoord =
                    TransformWorldToShadowCoord(input.positionWS);

                // 只获取主光源。
                Light mainLight =
                    GetMainLight(shadowCoord);

                half3 lightDirection =
                    normalize(mainLight.direction);

                half3 viewDirection =
                    normalize(
                        GetWorldSpaceViewDir(input.positionWS)
                    );

                // ==================================================
                // NPR 漫反射
                // ==================================================

                // cos(N, L)。
                half cosNL =
                    saturate(
                        dot(
                            normalWS,
                            lightDirection
                        )
                    );

                // 主光源阴影衰减。
                half shadow =
                    mainLight.shadowAttenuation;

                shadow=max(shadow,0.1);

                // 映射输入为 cos(N, L) * 阴影。
                half cosShadow =
                    cosNL * shadow;

                // 根据阈值切分成暗部和亮部。
                half band =
                    step(
                        kLambertThreshold,
                        cosShadow
                    );

                // 暗部映射到 kShadowBandValue，
                // 亮部映射到 kLightBandValue。
                half mappedLambert =
                    lerp(
                        kShadowBandValue,
                        kLightBandValue,
                        band
                    );

                // ==================================================
                // 高光
                // ==================================================

                half3 halfDirection =
                    normalize(
                        lightDirection +
                        viewDirection
                    );

                half cosNH =
                    saturate(
                        dot(
                            normalWS,
                            halfDirection
                        )
                    );

                // Smoothness 越高，高光越集中。
                half specularPower =
                    lerp(
                        0.01h,
                        256.0h,
                        _Smoothness
                    );

                half specularTerm =
                    pow(
                        cosNH,
                        specularPower
                    );

                // 避免背光面产生高光。
                specularTerm *= cosNL;

                // 高光受到阴影影响。
                specularTerm *= shadow;

                // 非金属 F0 为 0.04；
                // 金属高光颜色接近基础色。
                half3 specularColor =
                    lerp(
                        half3(0.04h, 0.04h, 0.04h),
                        _Color.rgb,
                        _Metallic
                    );

                half3 specular =
                    specularColor *
                    mainLight.color *
                    specularTerm;

                // 金属度越高，漫反射越弱。
                half diffuseWeight =
                    1.0h - _Metallic;

                half3 diffuse =
                    _Color.rgb *
                    mainLight.color *
                    mappedLambert *
                    diffuseWeight;

                half3 finalColor =
                    diffuse +
                    specular;

                finalColor=lerp(finalColor,half3(1,1,1),_WhiteOverlay);//闪白

                return half4(
                    finalColor,
                    _Color.a
                );
            }

            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"

            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM

            #pragma target 3.5

            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // 由 URP 的阴影渲染流程写入。
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 GetShadowPositionHClip(
                ShadowAttributes input)
            {
                float3 positionWS =
                    TransformObjectToWorld(
                        input.positionOS.xyz
                    );

                float3 normalWS =
                    TransformObjectToWorldNormal(
                        input.normalOS
                    );

                float3 lightDirectionWS;

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)

                    lightDirectionWS =
                        normalize(
                            _LightPosition -
                            positionWS
                        );

                #else

                    lightDirectionWS =
                        _LightDirection;

                #endif

                // 应用 URP 阴影偏移，避免 Shadow Acne。
                float3 biasedPositionWS =
                    ApplyShadowBias(
                        positionWS,
                        normalWS,
                        lightDirectionWS
                    );

                float4 positionCS =
                    TransformWorldToHClip(
                        biasedPositionWS
                    );

                // 防止应用偏移后穿过近平面。
                #if UNITY_REVERSED_Z

                    positionCS.z =
                        min(
                            positionCS.z,
                            UNITY_NEAR_CLIP_VALUE
                        );

                #else

                    positionCS.z =
                        max(
                            positionCS.z,
                            UNITY_NEAR_CLIP_VALUE
                        );

                #endif

                return positionCS;
            }

            ShadowVaryings ShadowVert(
                ShadowAttributes input)
            {
                ShadowVaryings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS =
                    GetShadowPositionHClip(input);

                return output;
            }

            half4 ShadowFrag(
                ShadowVaryings input) : SV_Target
            {
                return 0;
            }

            ENDHLSL
        }
    }

    FallBack Off
}