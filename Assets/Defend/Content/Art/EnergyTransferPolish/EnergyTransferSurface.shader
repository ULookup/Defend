Shader "Defend/Effects/EnergyTransferSurface"
{
    Properties
    {
        [HDR] _BaseColor("Color", Color) = (0.08, 0.8, 1, 1)
        [Enum(CrystalShell,0,CrystalCore,1,Ribbon,2,EndpointGlow,3)] _SurfaceKind("Surface", Float) = 0
        _Pulse("Explicit animation intensity", Range(0,2)) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "EnergyTransfer"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
            };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _SurfaceKind;
                float _Pulse;
                float _DstBlend;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 color = _BaseColor * input.color;
                if (_SurfaceKind < 1.5)
                {
                    half3 normal = normalize(input.normalWS);
                    half3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    half edge = pow(1 - saturate(abs(dot(normal, viewDirection))), 2.2);
                    half facet = .58 + .42 * saturate(dot(normal, normalize(half3(.35,.8,-.3))));
                    if (_SurfaceKind < .5)
                    {
                        color.rgb *= facet + edge * 1.4;
                        color.a *= .24 + edge * .7;
                    }
                    else
                    {
                        color.rgb *= .78 + facet * .36;
                    }
                }
                else if (_SurfaceKind < 2.5)
                {
                    // A ribbon uses transverse falloff without rounding its longitudinal ends.
                    float crossSection = abs(input.uv.y * 2 - 1);
                    color.a *= pow(saturate(1 - crossSection), 1.7);
                }
                else
                {
                    float radius = length(input.uv * 2 - 1);
                    color.a *= pow(saturate(1 - radius), 2.4);
                }
                color.a *= saturate(_Pulse);
                color.rgb *= .8 + .2 * _Pulse;
                return color;
            }
            ENDHLSL
        }
    }
}
