Shader "Defend/UI/ReplicaSurface"
{
    Properties
    {
        _Region ("Region", Float) = 0
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
            sampler2D _MainTex; float _Region;
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
                float2 uv=i.texcoord;
                float2 px=float2(uv.x*1672,(1-uv.y)*941);
                if(_Region<.5){
                  if(px.x>=45&&px.x<158&&px.y>=48&&px.y<71)uv.x=280.0/1672;
                  if(px.x>=600&&px.x<714&&px.y>12&&px.y<43 || px.x>=1050&&px.x<1138&&px.y>12&&px.y<43 || px.x>=1273&&px.x<1360&&px.y>14&&px.y<59 || px.x>=1455&&px.x<1528&&px.y>14&&px.y<59) uv.y=1-10.0/941;
                  if(px.x>=410&&px.x<706&&px.y>=41&&px.y<62 || px.x>=805&&px.x<1125&&px.y>=41&&px.y<62) {uv.x=720.0/1672;uv.y=1-49.0/941;}
                } else if(_Region<1.5){
                  if(px.y>=897&&px.y<928 && (px.x>=125&&px.x<178 || px.x>=304&&px.x<357 || px.x>=483&&px.x<536 || px.x>=662&&px.x<715 || px.x>=841&&px.x<894 || px.x>=1020&&px.x<1073))uv.y=1-(px.x>=662&&px.x<715?895.0:931.0)/941;
                } else if(_Region<2.5) {
                  if(px.x>=1389&&px.x<1645&&px.y>=383&&px.y<530)uv.x=1219.0/1672;
                  // The runtime owns the attribute table, energy row and state text.
                  // Keep the upper health overview, and remove the duplicate baked readout.
                  if(px.x>=1230&&px.x<1654&&px.y>=418&&px.y<638)uv.x=1219.0/1672;
                  if(px.x>=1315&&px.x<1645&&px.y>=559&&px.y<615)uv.y=1-620.0/941;
                  if(px.y>=638)uv.y=1-632.0/941;
                } else { if(px.x>=943&&px.x<1103&&px.y>=870&&px.y<900)uv.y=1-930.0/941; }
                fixed4 output=tex2D(_MainTex,uv)*i.color;
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








