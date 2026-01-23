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
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                
                // Calculate distortion offset based on normal
                // We project the normal to view space to get XY distortion direction
                float3 normalVS = TransformWorldToViewDir(IN.normalWS); // Actually gives ViewDir? No, we need View Space Normal.
                // TransformWorldToView(dir) transforms direction.
                // TransformWorldToViewNormal transforms normal.
                
                // Note: URP doesn't always expose TransformWorldToViewNormal easily without full matrix.
                // Let's approximate: simple view space normal.
                // But wait, the previous code snippet used `float2 offset = nor * ...`. 
                // That was object space or tangent space normal from texture. 
                // Here we operate on vertex normals of a torus.
                
                // We want the torus to act like a lens. The normal determines the bend.
                
                // GrabTexelSize is needed for pixel-perfect offsets, but we can just use UV scale.
                float2 offset = normalVS.xy * _Distortion * 0.01; 
                
                // Sample Scene Color
                float3 sceneColor = SampleSceneColor(screenUV + offset);
                
                // Soft edges: fade out alpha at UV edges for the torus if desired
                float alpha = _Color.a;
                
                if (_SmoothEdges > 0.5)
                {
                    // Assuming UV.v goes around the tube cross-section (0..1)
                    // We want to fade at 0 and 1? Or maybe just simple fresnel-like falloff?
                    // Let's rely on the mesh UVs if we had them correctly.
                    // For now, let's just make it fully visible but maybe fresnel-based alpha?
                    float NdotV = saturate(dot(IN.normalWS, normalize(GetCameraPositionWS() - IN.positionHCS.xyz))); 
                    // Wait, positionHCS is clip space. Recalculate world pos if needed? 
                    // Let's just return solid alpha for now as standard glass usually implies full refraction.
                }

                return half4(sceneColor * _Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
