Shader "Dangchenji/URP/Shell"
{
    Properties
    {
        [MainColor] _Color ("Shell colour", Color) = (0.82,0.79,0.68,1)
        _Specular ("Graphic highlight", Range(0,1)) = 0.25
        _Smoothness ("Highlight tightness", Range(0,1)) = 0.5
        _Rim ("Cyan rim", Range(0,1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        ZWrite On
        Cull Back
        Pass
        {
            Name "DC_ShellUnlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Specular;
                half _Smoothness;
                half _Rim;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                half3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 keyDirection = normalize(half3(-0.35h,0.8h,0.45h));
                half ndl = saturate(dot(n, keyDirection));
                half highlight = pow(saturate(dot(n, normalize(keyDirection + viewDirection))),
                                     lerp(12.0h,100.0h,_Smoothness));
                half rim = pow(1.0h - saturate(dot(n, viewDirection)),3.0h);
                half3 colour = _Color.rgb * (0.36h + 0.64h * ndl);
                colour += highlight * _Specular + rim * _Rim * half3(0.25h,0.83h,1.0h);
                return half4(colour,1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
