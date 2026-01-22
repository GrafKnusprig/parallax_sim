Shader "Custom/PlanetShadow"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
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

                float3 normal = normalize(IN.normalWS);
                
                // _SunDirection is the direction FROM the sun TO the planet/moon.
                // To get the light direction (TO the light), we revert it.
                // LightDir = -SunDirection
                float3 lightDir = normalize(-_SunDirection.xyz);

                // Lambertian Lighting
                // N dot L
                float NdotL = max(0, dot(normal, lightDir));

                // Apply Shadow and Ambient
                // When NdotL is 1 (facing sun), we want full color.
                // When NdotL is 0 (facing away), we want ambient + (1-shadowStrength) * color?
                // Simple model: LightIntensity = Ambient + NdotL
                
                float3 lighting = _AmbientColor.rgb + (NdotL * (1.0 - _ShadowStrength * 0.0)) + (1.0 - _ShadowStrength) * (1.0 - NdotL) * 0.1; 
                // Let's refine:
                // Shadowed area should be dark.
                // Light area should be bright.
                
                float lightIntensity = NdotL;
                
                // We want:
                // Value = Ambient + Light * (1 - ShadowStrength if shadowed) -- wait shadows are implicit here by N dot L
                
                // Better approach:
                // Direct Light = max(0, dot(N, L))
                // Final = Ambient + Direct * BaseColor
                
                // However user requested "shoud have a shadow". That implies the dark side is dark.
                // Base NdotL gives 0 on the back.
                
                float3 finalColor = baseColor.rgb * (_AmbientColor.rgb + lightIntensity);

                return half4(finalColor, baseColor.a);
            }
            ENDHLSL
        }
    }
}
