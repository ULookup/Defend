Shader "Defend/RapidFire/LayeredEffect"
{
    Properties
    {
        [HDR] _BaseColor("Color", Color) = (1, .45, .08, 1)
        _Mode("0 Flame, 1 Glow, 2 Streak, 3 Smoke", Float) = 1
        _Opacity("Opacity", Range(0, 1)) = 1
        _Phase("Authored animation phase", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Mode, _Opacity, _Phase, _DstBlend;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; output.color = input.color; return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                float2 p = input.uv * 2 - 1;
                half4 color = _BaseColor * input.color;
                float mask;
                if (_Mode < .5)
                {
                    float along = saturate(input.uv.y);
                    float profile = pow(saturate(sin((along * .94 + .025) * 3.14159265)), .65) * (1 - .6 * along);
                    float tongues = 1 + .14 * sin(along * 11 + _Phase) + .09 * sin(along * 21 - _Phase * 1.7);
                    float edge = abs(p.x + .04 * sin(along * 9 + _Phase));
                    mask = 1 - smoothstep(profile * .3, profile * tongues, edge);
                    mask *= smoothstep(0, .05, along) * (1 - smoothstep(.78, 1, along));
                    float hot = saturate(1 - edge / max(.05, profile)) * (1 - along * .65);
                    color.rgb = lerp(color.rgb, half3(2.5, 1.15, .28), hot * .35);
                }
                else if (_Mode < 1.5)
                {
                    float radius = length(p);
                    mask = pow(saturate(1 - radius), 1.7);
                }
                else if (_Mode < 2.5)
                {
                    mask = pow(saturate(1 - abs(p.y)), 1.6);
                    mask *= smoothstep(0, .09, input.uv.x) * (1 - smoothstep(.88, 1, input.uv.x));
                }
                else
                {
                    float radius = length(p);
                    float eddies = .82 + .1 * sin(p.x * 10 + p.y * 7) * sin(p.y * 13 - p.x * 5);
                    mask = pow(saturate(1 - radius / eddies), 1.6);
                }
                color.a *= mask * _Opacity;
                return color;
            }
            ENDHLSL
        }
    }
}
