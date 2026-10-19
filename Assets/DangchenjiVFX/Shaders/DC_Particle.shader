Shader "Dangchenji/URP/Particle"
{
    Properties
    {
        [MainTexture] _MainTex ("Straight-alpha particle texture", 2D) = "white" {}
        [HDR] [MainColor] _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("RGB intensity", Range(0,8)) = 1
        [Toggle] _SwapUV ("Swap U/V (trail only)", Float) = 0
        [HideInInspector] _Premultiply ("Premultiply RGB in shader", Float) = 1
        [HideInInspector] _SrcBlend ("Source blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        Pass
        {
            Name "DC_Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Intensity;
                half _Premultiply;
                half _SwapUV;
                half _SrcBlend;
                half _DstBlend;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float2 uv = lerp(input.uv, input.uv.yx, _SwapUV);
                // WholeSheet provides top-left-first atlas UVs. No extra row reversal.
                output.uv = uv * _MainTex_ST.xy + _MainTex_ST.zw;
                output.color = input.color * _Color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 result = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                result.a = saturate(result.a);
                result.rgb *= _Intensity;
                result.rgb *= lerp(1.0h, result.a, _Premultiply);
                return result;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
