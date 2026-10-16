Shader "Defend/Placement/Ground"
{
    Properties
    {
        _BaseColor("颜色", Color) = (.25,.8,.9,1)
        [Enum(Outline,0,EfficiencyCell,1,Footprint,2)] _Pattern("地面图形", Float) = 0
        _Dashed("原位置虚线", Float) = 0
        _Percentage("运输效率", Range(0,1)) = 1
        _FillOpacity("填充透明度", Range(0,1)) = .36
        _MinimumEfficiency("范围边缘效率", Range(0,1)) = .65
        _LowEfficiencyColor("低效率颜色", Color) = (.32,.22,.85,1)
        _MidEfficiencyColor("中间效率颜色", Color) = (.06,.50,.94,1)
        _HighEfficiencyColor("高效率颜色", Color) = (.10,.95,.62,1)
        _ShouldBeDisplayed("显示格子", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
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
                half4 _LowEfficiencyColor, _MidEfficiencyColor, _HighEfficiencyColor;
                float _Pattern, _Dashed, _Percentage, _ShouldBeDisplayed, _MinimumEfficiency, _FillOpacity;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv; output.color = input.color; return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                half4 color = _BaseColor * input.color;
                float edge = min(min(input.uv.x, 1-input.uv.x), min(input.uv.y, 1-input.uv.y));
                if (_Pattern > 1.5)
                    color.a *= lerp(.055, .8, 1-smoothstep(.025, .055, edge));
                else if (_Pattern > .5)
                {
                    float efficiencyBand = _MinimumEfficiency >= .9999 ? 1
                        : saturate((_Percentage - _MinimumEfficiency) / (1 - _MinimumEfficiency));
                    color.rgb = efficiencyBand < .5
                        ? lerp(_LowEfficiencyColor.rgb, _MidEfficiencyColor.rgb, efficiencyBand * 2)
                        : lerp(_MidEfficiencyColor.rgb, _HighEfficiencyColor.rgb, (efficiencyBand - .5) * 2);
                    color.a *= _ShouldBeDisplayed * _FillOpacity * lerp(.75, 1, smoothstep(0, .018, edge));
                }
                else
                {
                    color.a *= smoothstep(0, .2, input.uv.y) * smoothstep(0, .2, 1-input.uv.y);
                    if (_Dashed > .5) color.a *= step(.38, frac(input.uv.x * 48));
                }
                return color;
            }
            ENDHLSL
        }
    }
}
