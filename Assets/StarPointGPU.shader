Shader "Custom/StarPointGPU"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Brightness ("Brightness", Float) = 1.0
        _Size ("Size", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            // Star data from compute shader
            struct StarData
            {
                float3 worldPosition;
            };
            
            StructuredBuffer<StarData> _VisibleStars;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
                UNITY_VERTEX_INPUT_INSTANCE_ID // VR optimization
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO // VR optimization
            };

            fixed4 _Color;
            float _Brightness;
            float _Size;

            v2f vert(appdata v)
            {
                v2f o;
                
                // Only initialize stereo output - don't use UNITY_SETUP_INSTANCE_ID
                // because v.instanceID is used for buffer indexing, not Unity's GPU instancing
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                // Get star data from buffer
                StarData star = _VisibleStars[v.instanceID];
                float3 centerWorldPos = star.worldPosition;
                
                // Calculate billboard vectors for camera-facing quads
                float3 cameraPos = _WorldSpaceCameraPos;
                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    cameraPos = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
                #endif
                float3 viewDir = normalize(cameraPos - centerWorldPos);
                float3 upDir = float3(0, 1, 0);
                float3 rightDir = normalize(cross(upDir, viewDir));
                upDir = normalize(cross(viewDir, rightDir));
                
                // Scale based on distance
                float dist = length(cameraPos - centerWorldPos);
                float scale = _Size * max(0.1, dist * 0.0001);
                
                // Apply billboard transformation
                float3 localPos = v.vertex.xyz * scale;
                float3 worldPos = centerWorldPos + rightDir * localPos.x + upDir * localPos.y;
                
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.uv = v.uv;
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                
                // Create circular star with glow
                float2 center = float2(0.5, 0.5);
                float dist = distance(i.uv, center);
                
                // Core (bright center)
                float core = 1.0 - smoothstep(0.0, 0.15, dist);
                
                // Outer glow
                float glow = 1.0 - smoothstep(0.0, 0.5, dist);
                glow = pow(glow, 1.5);
                
                // Combine core and glow
                float alpha = core + glow * 0.5;
                
                // Apply brightness to color
                fixed4 col = _Color;
                col.rgb *= _Brightness;
                col.a = alpha;
                col.a = max(col.a, 0.02 * glow);
                
                return col;
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
