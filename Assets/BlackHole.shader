Shader "Custom/BlackHole"
{
    Properties
    {
        [Header(Black Hole)]
        _EventHorizonColor ("Event Horizon Color", Color) = (0,0,0,1)
        _HorizonRadius ("Horizon Radius (Fresnel)", Range(0.0, 1.0)) = 0.5
        _Softness ("Horizon Softness", Range(0.001, 0.5)) = 0.1

        [Header(Photosphere)]
        _PhotosphereColor ("Photosphere Color", Color) = (1, 0.5, 0.2, 1)
        _PhotosphereIntensity ("Photosphere Intensity", Range(0, 10)) = 2.0
        _PhotosphereThickness ("Photosphere Thickness", Range(0.0, 1.0)) = 0.1

        [Header(Accretion Edge)]
        _EdgeGlowColor ("Edge Glow Color", Color) = (0.0, 0.5, 1.0, 1)
        _EdgeGlowPower ("Edge Glow Power", Range(0.1, 10.0)) = 3.0
        _EdgeGlowIntensity ("Edge Glow Intensity", Range(0.0, 10.0)) = 2.0

        [Header(Lensing)]
        _DistortionStrength ("Optical Distortion Strength", Range(-2.0, 2.0)) = -1.0
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
            
            // Standard transparency blending
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

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
                float3 viewDir     : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _EventHorizonColor;
            float  _HorizonRadius;
            float  _Softness;
            float4 _PhotosphereColor;
            float  _PhotosphereIntensity;
            float  _PhotosphereThickness;
            float4 _EdgeGlowColor;
            float  _EdgeGlowPower;
            float  _EdgeGlowIntensity;
            float  _DistortionStrength;
            CBUFFER_END

            // ==================== VERTEX SHADER ====================

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPos    = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.worldPos);
                OUT.viewDir     = GetCameraPositionWS() - OUT.worldPos;
                
                // Calculate screen position for grab pass sampling
                OUT.screenPos   = ComputeScreenPos(OUT.positionHCS);

                return OUT;
            }

            // ==================== FRAGMENT SHADER ====================

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                
                float3 N = normalize(IN.worldNormal);
                float3 V = normalize(IN.viewDir);
                
                // ViewFactor: 1.0 at center (facing camera), 0.0 at edge
                float viewFactor = saturate(dot(N, V));
                
                // Fresnel: 0.0 at center, 1.0 at edge
                float fresnel = 1.0 - viewFactor;

                // ==================== OPTICAL DISTORTION ====================
                
                // Calculate screen UVs
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                
                // Distort UVs based on normal view space projection (approximation for lens)
                // Strength falls off towards edge to avoid hard cut (?) 
                // Actually for a sphere lens, distortion is highest where light bends most.
                // Simple refraction: offset by normal xy.
                
                float3 normalVS = TransformWorldToViewDir(N);
                float2 offset = normalVS.xy * _DistortionStrength; 
                
                float2 distortedUV = uv + offset * 0.1; // Scale factor adjustment
                
                float3 sceneColor = SampleSceneColor(distortedUV);

                // ==================== BLACK HOLE CORE ====================
                
                // Core is visible where viewFactor is high (center of sphere)
                // We use viewFactor (1 center, 0 edge). 
                // If viewFactor > (1.0 - _HorizonRadius), we are inside the black hole.
                
                float horizonThreshold = 1.0 - _HorizonRadius;
                // Fix: smoothstep(min, max, x). We want 1 when x > threshold.
                // We want transition from (threshold - softness) to threshold.
                float holeAlpha = smoothstep(horizonThreshold - _Softness, horizonThreshold, viewFactor);
                // holeAlpha is 1 at center, 0 outside.
                
                // ==================== PHOTOSPHERE ====================
                
                // Ring around the horizon
                // Centered at horizonThreshold
                float photoDist = abs(viewFactor - horizonThreshold);
                // Invert: 1.0 at center distance, 0.0 far away
                float photoMask = 1.0 - smoothstep(0.0, _PhotosphereThickness, photoDist);
                float3 photoGlow = _PhotosphereColor.rgb * photoMask * _PhotosphereIntensity;

                // ==================== EDGE GLOW ====================
                
                float edgeFresnel = pow(fresnel, _EdgeGlowPower);
                float3 edgeGlow = _EdgeGlowColor.rgb * edgeFresnel * _EdgeGlowIntensity;

                // ==================== COMPOSITING ====================
                
                // Start with distorted scene
                float3 finalColor = sceneColor;
                
                // Apply Black Hole Core (Overwrite with black)
                // Usage of lerp ensures we replace the background with the black color
                finalColor = lerp(finalColor, _EventHorizonColor.rgb, holeAlpha * _EventHorizonColor.a);
                
                // Add Photosphere (On top of everything)
                finalColor += photoGlow;
                
                // Add Edge Glow (On top of everything)
                finalColor += edgeGlow;
                
                // Ensure alpha is 1.0 so we overwrite the buffer (since we used OneMinusSrcAlpha blending logic above effectively)
                return half4(finalColor, 1.0); 
            }

            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Unlit"
}
