Shader "Defend/UI/ReplicaLabel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Reference label sprite", 2D) = "white" {}
        _Color ("Text tint", Color) = (1,1,1,1)
        [Toggle] _DarkTextSource ("Source has dark text on a light background", Float) = 0
        _Threshold ("Glyph coverage threshold", Range(0,1)) = .45
        _Softness ("Glyph antialias transition", Range(.001,.5)) = .25
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; float4 worldPosition:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            float4 _MainTex_ST, _ClipRect;
            fixed4 _Color;
            float _DarkTextSource, _Threshold, _Softness;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex);
                o.texcoord=TRANSFORM_TEX(v.texcoord,_MainTex); o.color=v.color*_Color;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 source=tex2D(_MainTex,i.texcoord);
                float3 displayColor=source.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                displayColor=LinearToGammaSpace(displayColor);
                #endif
                // 使用原图字形亮度提取覆盖率，不采纳原图背景或文字颜色。
                float luminance=dot(displayColor,float3(.2126,.7152,.0722));
                float glyph=lerp(luminance,1-luminance,saturate(_DarkTextSource));
                float coverage=smoothstep(_Threshold-_Softness,_Threshold+_Softness,glyph);
                fixed4 output=i.color;
                output.a*=coverage*source.a;
                #ifdef UNITY_UI_CLIP_RECT
                output.a*=UnityGet2DClipping(i.worldPosition.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(output.a-.001);
                #endif
                return output;
            }
            ENDCG
        }
    }
}
