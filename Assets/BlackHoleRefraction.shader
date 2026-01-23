Shader "Custom/BlackHoleRefraction"
{
    Properties
    {
        _Color ("Tint Color", Color) = (1,1,1,1)
        _Distortion ("Distortion Strength", Range(0, 50)) = 10.0
        [Toggle] _SmoothEdges ("Smooth Edges (Alpha Falloff)", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+100" // Draw after most transparent objects but before overlays
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "Refraction"
            Tags { "LightMode"="UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPos   : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float  _Distortion;
            float  _SmoothEdges;
            float  _VisibilityUVLimit; // New property
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.screenPos   = ComputeScreenPos(OUT.positionHCS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = IN.uv;

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // 1. Transparency Mask based on Radial UV (IN.uv.y)
                // We physically generated the mesh larger (1.5x) but only want to render/refract
                // the inner part covering the accretion disc.
                // If uv.y is beyond the limit, we are in the "Star Field" zone -> Invisible.
                
                float limit = _VisibilityUVLimit; 
                // Default to 1.0 if not set (safe fallback) but ideally set by script.
                if (limit <= 0.001) limit = 1.0; 
                
                // Soft mask for nicer edge
                float mask = 1.0 - smoothstep(limit - 0.05, limit, IN.uv.y);
                
                if (mask < 0.01) discard; // Optimization
                
                // 2. Refraction Logic
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float3 normalVS = TransformWorldToViewDir(IN.normalWS); 
                float2 offset = normalVS.xy * _Distortion * 0.01; 
                
                // Apply Mask to Distortion Strength as well? 
                // Yes, unwanted distortion at the fade edge looks bad.
                float2 finalOffset = offset * mask;
                
                float3 sceneColor = SampleSceneColor(screenUV + finalOffset);
                
                // 3. Output
                // We want the torus itself to be transparent (alpha * mask).
                // Actually, if mask is 0, we discared.
                // The visible part applies refraction.
                
                return half4(sceneColor * _Color.rgb, _Color.a * mask);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
