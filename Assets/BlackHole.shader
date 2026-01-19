Shader "Custom/BlackHole"
{
    Properties
    {
        // Event Horizon
        _EventHorizonColor ("Event Horizon Color", Color) = (0,0,0,1)
        _PhotosphereColor ("Photosphere Color", Color) = (0.3, 0.15, 0.05, 1)
        _PhotosphereRadius ("Photosphere Radius", Range(1.0, 2.5)) = 1.35
        _PhotosphereIntensity ("Photosphere Intensity", Range(0, 5)) = 1.5
        
        // Gravitational Lensing
        _LensingStrength ("Lensing Strength", Range(0, 2)) = 0.8
        _LensingRadius ("Lensing Radius", Range(0.8, 1.5)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Geometry"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            Blend One One
            ZWrite On
            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 objectPos   : TEXCOORD2;
                float3 viewDir     : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _EventHorizonColor;
            float4 _PhotosphereColor;
            float  _PhotosphereRadius;
            float  _PhotosphereIntensity;
            float  _LensingStrength;
            float  _LensingRadius;
            CBUFFER_END

            // ==================== NOISE FUNCTIONS ====================
            
            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = hash(i + float3(0,0,0));
                float n100 = hash(i + float3(1,0,0));
                float n010 = hash(i + float3(0,1,0));
                float n110 = hash(i + float3(1,1,0));
                float n001 = hash(i + float3(0,0,1));
                float n101 = hash(i + float3(1,0,1));
                float n011 = hash(i + float3(0,1,1));
                float n111 = hash(i + float3(1,1,1));

                float n00 = lerp(n000, n100, u.x);
                float n10 = lerp(n010, n110, u.x);
                float n01 = lerp(n001, n101, u.x);
                float n11 = lerp(n011, n111, u.x);

                float n0 = lerp(n00, n10, u.y);
                float n1 = lerp(n01, n11, u.y);

                return lerp(n0, n1, u.z);
            }

            float fbm(float3 p)
            {
                float v = 0;
                float a = 0.5;

                for (int i = 0; i < 4; i++)
                {
                    v += a * noise3D(p);
                    p *= 2.0;
                    a *= 0.5;
                }
                return v;
            }

            // ==================== VERTEX SHADER ====================

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPos    = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.worldPos);
                OUT.objectPos   = IN.positionOS.xyz;
                OUT.viewDir     = normalize(GetCameraPositionWS() - OUT.worldPos);

                return OUT;
            }

            // ==================== FRAGMENT SHADER ====================

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                
                float3 N = normalize(IN.worldNormal);
                float3 V = normalize(IN.viewDir);
                float3 objPos = normalize(IN.objectPos);
                
                // Distance from center (in object space)
                float dist = length(IN.objectPos);
                
                // ==================== PHOTOSPHERE (HOT PLASMA NEAR HORIZON) ====================
                
                // Photosphere is a thin glowing layer just outside event horizon
                float photoDist = abs(dist - _PhotosphereRadius);
                float photoMask = 1.0 - saturate(photoDist * 5.0);
                photoMask = pow(photoMask, 2.0);
                
                // Add some turbulence to photosphere
                float photoNoise = fbm(objPos * 8.0 + _Time.y * 0.2);
                photoMask *= (0.5 + 0.5 * photoNoise);
                
                float3 photoGlow = _PhotosphereColor.rgb * photoMask * _PhotosphereIntensity * 20.0;
                
                // ==================== GRAVITATIONAL LENSING ====================
                
                // Simple lensing effect: brighten edges based on viewing angle
                float rimAngle = 1.0 - abs(dot(N, V));
                float lensingDist = abs(dist - _LensingRadius);
                float lensingMask = saturate(1.0 - lensingDist / 0.5);
                float lensing = pow(rimAngle, 2.0) * lensingMask * _LensingStrength;
                
                // ==================== FINAL COMPOSITION ====================
                
                float3 finalColor = float3(0, 0, 0);
                
                // Add photosphere glow
                finalColor += photoGlow;
                
                // Add lensing enhancement
                finalColor += photoGlow * lensing * 2.0;
                
                // Event horizon is pure black and always visible in center
                float eventHorizonMask = smoothstep(1.2, 1.0, dist);
                finalColor = lerp(finalColor, _EventHorizonColor.rgb, eventHorizonMask);
                
                return half4(finalColor, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
