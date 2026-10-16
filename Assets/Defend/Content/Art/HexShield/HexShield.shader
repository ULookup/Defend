Shader "Defend/Effects/Hexagonal Shield"
{
    Properties
    {
        [HDR] _PanelColor("蓝紫板片", Color) = (.12,.18,.95,1)
        [HDR] _LineColor("冰蓝描边", Color) = (.42,.66,1.8,1)
        [HDR] _RimColor("粉白金轮廓", Color) = (2.5,1.2,.95,1)
        _Energy("真实储能比例", Range(0,1)) = 1
        _ImpactBlend("受击聚合", Range(0,1)) = 0
        _PresentationClock("表现时间", Float) = 0
        _HitAge("命中后的时间", Float) = 20
        _HitDirection("命中方向", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Name "HexagonalPanels"
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
                half4 _PanelColor, _LineColor, _RimColor;
                float4 _HitDirection;
                float _Energy, _ImpactBlend, _PresentationClock, _HitAge;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 radialSeed : TEXCOORD0;
                float4 cellCenter : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 radialSeed : TEXCOORD2;
                float3 cellNormal : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float seed = input.radialSeed.y;
                float assembly = smoothstep(0, 1, _ImpactBlend);
                float breath = sin(_PresentationClock * 1.3962634 + seed * 18.8496);
                float drift = sin(_PresentationClock * .7853982 + seed * 31.4159);
                float spread = (1 - assembly) * (.08 + .26 * seed + breath * .022 + drift * .012);
                float scale = lerp(.77 + breath * .025, .975, assembly);
                float3 center = input.cellCenter.xyz;
                float3 offset = input.positionOS.xyz - center;
                // 每块胞格沿自身法线独立分离，收拢后仍保留精细接缝。
                // 给描边留出少量几何余量，使柔光可以越过板片边缘。
                float3 position = center * (1 + spread) + offset * scale * 1.08;
                output.positionWS = TransformObjectToWorld(position);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.radialSeed = float2(input.radialSeed.x * 1.08, seed);
                output.cellNormal = normalize(center);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float radial = input.radialSeed.x;
                float seed = input.radialSeed.y;
                float assembly = smoothstep(0, 1, _ImpactBlend);
                float width = max(fwidth(radial) * .8, .006);
                float outline = 1 - smoothstep(.019, .019 + width, abs(radial - .965));
                float innerLine = 1 - smoothstep(.014, .014 + width, abs(radial - .79));
                float echoLine = 1 - smoothstep(.007, .007 + width, abs(radial - .59));
                float halo = exp2(-abs(radial - .965) * 34) * .42;
                float innerHalo = exp2(-abs(radial - .79) * 50) * .16;
                float panelMask = 1 - smoothstep(.95, .985, radial);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float facing = dot(normalize(input.normalWS), view);
                float front = smoothstep(-.28, .05, facing);
                float backFade = lerp(.12, 1, front);
                float rim = pow(1 - abs(facing), 2.1);
                float accent = smoothstep(.78, .92, seed) * (1 - assembly) * .82;
                float warm = max(smoothstep(.31, .78, rim), accent);
                float pulse = exp2(-_HitAge * 9);
                float hasDirection = step(.0001, dot(_HitDirection.xyz, _HitDirection.xyz));
                float3 direction = _HitDirection.xyz * rsqrt(max(.0001, dot(_HitDirection.xyz, _HitDirection.xyz)));
                float contactDistance = 1 - dot(normalize(input.cellNormal), direction);
                float wave = hasDirection * exp2(-abs(contactDistance - _HitAge * 2.6) * 19) * exp2(-_HitAge * 2.8);
                float shimmer = .9 + .1 * sin(_PresentationClock * 1.3962634 + seed * 18.8496);
                float lines = outline + innerLine * .5 + echoLine * lerp(.12, .045, assembly);
                float glow = halo + innerHalo;
                float panelOpacity = lerp(.075, .29, assembly) + warm * lerp(.39, .44, assembly);
                float alpha = saturate(panelOpacity * panelMask + lines * lerp(.56, .74, assembly) + glow);
                float3 panel = lerp(_PanelColor.rgb * float3(.95, .78, 1.65),
                    _RimColor.rgb * float3(1.2, .78, 1.08), warm);
                float3 edge = lerp(_LineColor.rgb * float3(1, 1.05, 1.3),
                    _RimColor.rgb * float3(1.4, .85, 1.18), warm);
                float3 color = lerp(panel, edge, saturate(lines + glow * 1.2));
                color *= shimmer * lerp(.65, 1, _Energy) * (1 + assembly * .28 + pulse * .65 + wave * .55);
                float haloBoundary = 1 - smoothstep(1.025, 1.08, radial);
                return half4(color, alpha * haloBoundary * backFade * step(.0001, _Energy));
            }
            ENDHLSL
        }
    }
}
