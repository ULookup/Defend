Shader "Defend/Effects/Hexagonal Shield Particles"
{
    Properties
    {
        [HDR] _ParticleBlue("蓝紫微光", Color) = (.48,.62,2.4,1)
        [HDR] _ParticleWarm("粉金微光", Color) = (2.4,.95,1.2,1)
        _Energy("真实储能比例", Range(0,1)) = 1
        _ImpactBlend("受击聚合", Range(0,1)) = 0
        _PresentationClock("表现时间", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+11" }
        Pass
        {
            Name "FloatingHexagons"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ParticleBlue, _ParticleWarm;
                float _Energy, _ImpactBlend, _PresentationClock;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 corner : TEXCOORD0;
                float2 seedSize : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float2 seedFade : TEXCOORD1;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                float seed = input.seedSize.x;
                float assembly = smoothstep(0, 1, _ImpactBlend);
                float phase = seed * 6.2831853;
                float age = _PresentationClock * .6981317 + phase;
                float angle = _PresentationClock * .06981317;
                float sine, cosine;
                sincos(angle, sine, cosine);
                float3 direction = input.positionOS.xyz;
                direction.xz = float2(direction.x * cosine - direction.z * sine, direction.x * sine + direction.z * cosine);
                float radius = lerp(1.10 + seed * .28 + sin(age) * .035, 1.02, assembly);
                float3 position = direction * radius;
                position.y += sin(_PresentationClock * .5235988 + phase * 2) * .045 * (1 - assembly);
                float3 positionWS = TransformObjectToWorld(position);
                float3 positionVS = TransformWorldToView(positionWS);
                float scale = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                float size = input.seedSize.y * (.84 + .16 * sin(age));
                positionVS.xy += input.corner * size * scale;
                output.positionCS = mul(UNITY_MATRIX_P, float4(positionVS, 1));
                output.corner = input.corner;
                output.seedFade = float2(seed, (.55 + .45 * sin(age) * sin(age)) * lerp(1, .28, assembly));
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 hexPosition = abs(input.corner);
                float distance = max(hexPosition.x * .8660254 + hexPosition.y * .5, hexPosition.y);
                float edge = abs(distance - .60);
                float width = max(fwidth(distance), .018);
                float outline = 1 - smoothstep(.035, .035 + width, edge);
                float halo = exp2(-edge * 21) * .33;
                // 以空心六边形为主，少量极小的中心微光打破重复感。
                float sparkle = (1 - smoothstep(.02, .16, length(input.corner))) * step(.82, input.seedFade.x);
                float boundary = 1 - smoothstep(.78, .94, length(input.corner));
                float alpha = saturate(outline * .74 + halo + sparkle * .48) * boundary * input.seedFade.y;
                float3 color = lerp(_ParticleBlue.rgb, _ParticleWarm.rgb, step(.72, input.seedFade.x));
                return half4(color, alpha * lerp(.65, 1, _Energy) * step(.0001, _Energy));
            }
            ENDHLSL
        }
    }
}
