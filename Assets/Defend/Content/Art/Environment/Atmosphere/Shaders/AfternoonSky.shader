Shader "Defend/Environment/AfternoonSky"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Photographic Sky Panorama", 2D) = "grey" {}
        _Tint ("Sky Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Sky Exposure", Range(0, 4)) = 1.25
        _Rotation ("Panorama Sampling Rotation", Range(0, 360)) = 20.9
        _HorizonColor ("Distant Atmospheric Blue", Color) = (0.58, 0.75, 0.90, 1)
        _LowerSkyColor ("Lower Atmospheric Blue", Color) = (0.28, 0.52, 0.79, 1)
        _LowerHemisphereBlend ("Lower Hemisphere Atmospheric Haze", Range(0, 1)) = 0.75
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _MainTex_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            half4 _HorizonColor;
            half4 _LowerSkyColor;
            half _LowerHemisphereBlend;

            struct Attributes
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                float angle = _Rotation * UNITY_PI / 180.0;
                float sine, cosine;
                sincos(angle, sine, cosine);
                output.direction = float3(
                    cosine * input.vertex.x - sine * input.vertex.z,
                    input.vertex.y,
                    sine * input.vertex.x + cosine * input.vertex.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.direction);
                float lowerDepth = saturate(-direction.y);

                // A ground-level panorama's painted lower hemisphere is unsuitable
                // for a floating island. Continue its real cloud forms into blue
                // aerial haze, without sampling terrain or the retouched ground.
                // Stay below the photographed sun in the lower continuation so
                // the scene never acquires a second sun beneath the islands.
                float panoramaElevation = direction.y >= 0.0 ? direction.y : lowerDepth * 0.55;
                float longitude = atan2(direction.z, direction.x);
                float latitude = acos(clamp(panoramaElevation, -1.0, 1.0));
                float2 coordinates = float2(0.5 - longitude / (2.0 * UNITY_PI), 1.0 - latitude / UNITY_PI);
                half3 photographicSky = DecodeHDR(tex2D(_MainTex, coordinates), _MainTex_HDR) * _Tint.rgb;

                half3 aerialBlue = lerp(_HorizonColor.rgb, _LowerSkyColor.rgb, smoothstep(0.0, 0.92, lowerDepth));
                half haze = smoothstep(0.0, 0.32, lowerDepth) * _LowerHemisphereBlend;
                haze = lerp(haze, 1.0h, smoothstep(0.92, 1.0, lowerDepth));
                half3 sky = lerp(photographicSky, aerialBlue, haze);
                return half4(sky * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
