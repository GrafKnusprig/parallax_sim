Shader "Custom/AccretionDisc"
{
    Properties
    {
        _DiscColor ("Disc Base Color", Color) = (1, 0.5, 0.1, 1)
        _DiscColorHot ("Hot Side (blue-shifted)", Color) = (0.8, 0.9, 1.0, 1)
        _DiscColorCold ("Cold Side (red-shifted)", Color) = (1.0, 0.3, 0.05, 1)
        _Intensity ("Intensity", Range(0, 20)) = 8.0
        _RotationSpeed ("Rotation Speed", Float) = 0.4
        _NoiseScale ("Noise Scale", Float) = 5.0
        _Turbulence ("Turbulence", Range(0, 1)) = 0.65
        _DopplerIntensity ("Doppler Shift", Range(0, 2)) = 1.2
        _InnerFade ("Inner Fade", Range(0, 1)) = 0.3
        _OuterFade ("Outer Fade", Range(0, 1)) = 0.8
        _InnerGlow ("Inner Glow Boost", Range(1, 5)) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _DiscColor;
            float4 _DiscColorHot;
            float4 _DiscColorCold;
            float  _Intensity;
            float  _RotationSpeed;
            float  _NoiseScale;
            float  _Turbulence;
            float  _DopplerIntensity;
            float  _InnerFade;
            float  _OuterFade;
            float  _InnerGlow;
            CBUFFER_END

            // Noise functions
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

                return lerp(
                    lerp(lerp(hash(i + float3(0,0,0)), hash(i + float3(1,0,0)), u.x),
                         lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), u.x), u.y),
                    lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), u.x),
                         lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), u.x), u.y),
                    u.z);
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

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPos    = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.worldPos);
                OUT.uv          = IN.uv;

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                
                // UV.x is radial (0=inner, 1=outer), UV.y is angular
                float radial = IN.uv.x;
                float angular = IN.uv.y;
                
                // Convert to angle
                float angle = angular * 2.0 * 3.14159265;
                
                // Keplerian rotation (inner faster than outer) - v ∝ 1/√r
                float rotationSpeed = _RotationSpeed / sqrt(radial + 0.1);
                float rotatedAngle = angle + _Time.y * rotationSpeed;
                
                // Doppler shift based on rotation direction (subtle effect)
                float dopplerShift = cos(angle) * _DopplerIntensity;
                
                // Base color from radial position (inner hotter = more orange/white, outer cooler = darker orange)
                float3 baseColor = lerp(_DiscColor.rgb, _DiscColor.rgb * 0.5, radial);
                
                // Apply subtle Doppler tint
                float3 discColor = lerp(baseColor, _DiscColorHot.rgb, dopplerShift * 0.5 + 0.5);
                discColor = lerp(discColor, _DiscColorCold.rgb, (-dopplerShift) * 0.3);
                
                // Turbulent noise with rotation
                float3 noisePos = float3(cos(rotatedAngle) * radial, rotatedAngle * 2.0, sin(rotatedAngle) * radial);
                float noise = fbm(noisePos * _NoiseScale + float3(0, _Time.y * 0.3, 0));
                
                // Prominent spiral arms pattern (more visible)
                float spiralPattern = sin(rotatedAngle * 4.0 - radial * 15.0);
                spiralPattern = saturate(spiralPattern * 0.5 + 0.5);
                spiralPattern = pow(spiralPattern, 2.0); // Sharper spiral arms
                
                // Add discrete bright clumps/hot spots that rotate
                float clumpPattern = sin(rotatedAngle * 8.0 + radial * 5.0) * cos(rotatedAngle * 6.0);
                clumpPattern = pow(saturate(clumpPattern * 0.5 + 0.5), 4.0);
                
                // Combine turbulence with visible structures
                float structures = lerp(spiralPattern, clumpPattern, 0.3);
                float turbulence = lerp(0.7, noise * structures, _Turbulence);
                
                // Radial fading (fade at inner and outer edges)
                float innerFade = smoothstep(0.0, _InnerFade, radial);
                float outerFade = smoothstep(1.0, _OuterFade, radial);
                float fadeMask = innerFade * outerFade;
                
                // Inner regions glow brighter (hotter closer to black hole)
                float innerGlow = 1.0 + (1.0 - radial) * _InnerGlow;
                
                // Enhance brightness variation for visible rotation
                float variationBoost = 1.0 + structures * 0.8;
                
                // Final brightness
                float brightness = turbulence * fadeMask * innerGlow * variationBoost * _Intensity;
                
                float3 finalColor = discColor * brightness;
                float alpha = fadeMask * turbulence;
                
                return half4(finalColor, alpha);
            }

            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
