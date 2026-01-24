Shader "Custom/PlanetShadow"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _SecondTex("Second Texture", 2D) = "black" {} // Default to black for no effect
        _NightTex("Night Texture", 2D) = "black" {}
        _SunDirection("Sun Direction", Vector) = (1, 0, 0, 0)
        _AmbientColor("Ambient Color", Color) = (0.1, 0.1, 0.1, 1)
        _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.8

        [Header(Stencil)]
        _StencilRef("Stencil Reference", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Comparison", Float) = 8 // Always
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass("Stencil Pass", Float) = 0 // Keep
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _SunDirection;
                float4 _AmbientColor;
                float _ShadowStrength;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            TEXTURE2D(_SecondTex);
            TEXTURE2D(_NightTex);
            SAMPLER(sampler_BaseMap);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 secondColor = SAMPLE_TEXTURE2D(_SecondTex, sampler_BaseMap, IN.uv);
                half4 nightColor = SAMPLE_TEXTURE2D(_NightTex, sampler_BaseMap, IN.uv);

                // Apply Screen Blend Mode: 1 - (1 - Base) * (1 - Blend)
                // If secondColor is black (default), this results in baseColor.
                half3 blendedColor = 1.0 - (1.0 - baseColor.rgb) * (1.0 - secondColor.rgb);
                
                baseColor.rgb = blendedColor;

                float3 normal = normalize(IN.normalWS);
                
                // _SunDirection is the direction FROM the sun TO the planet/moon.
                // To get the light direction (TO the light), we revert it.
                // LightDir = -SunDirection
                float3 lightDir = normalize(-_SunDirection.xyz);

                // Lambertian Lighting
                float rawNdotL = dot(normal, lightDir);
                float NdotL = max(0, rawNdotL);

                // Apply Shadow and Ambient
                // This applies to the day-side surface (Base + Atmosphere)
                float3 finalColor = baseColor.rgb * (_AmbientColor.rgb + NdotL);

                // Calculate Night Logic
                // Night lights appear where rawNdotL is negative (shadow side).
                // They should be occluded by the atmosphere (SecondTex).
                // smoothstep(0.0, 0.2, -rawNdotL) makes it fade in as we go into shadow.
                float nightFactor = smoothstep(0.0, 0.2, -rawNdotL);
                float3 nightLights = nightColor.rgb * nightFactor * (1.0 - secondColor.rgb);

                finalColor += nightLights;

                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }
    }
}
