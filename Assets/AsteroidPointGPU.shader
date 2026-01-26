Shader "Custom/AsteroidPointGPU"
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

            // Asteroid data from compute shader
            struct AsteroidData
            {
                float3 worldPosition;
            };
            
            StructuredBuffer<AsteroidData> _VisibleAsteroids;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _Brightness;
            float _Size;

            v2f vert(appdata v)
            {
                v2f o;
                
                // Get asteroid data from buffer
                AsteroidData asteroid = _VisibleAsteroids[v.instanceID];
                float3 centerWorldPos = asteroid.worldPosition;
                
                // Calculate billboard vectors for camera-facing quads
                float3 cameraPos = _WorldSpaceCameraPos;
                #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                    cameraPos = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
                #endif
                float3 viewDir = normalize(cameraPos - centerWorldPos);
                float3 upDir = float3(0, 1, 0);
                float3 rightDir = normalize(cross(upDir, viewDir));
                upDir = normalize(cross(viewDir, rightDir));
                
                // Scale based on distance (optional, similar to stars)
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
                // Create circular asteroid with falloff
                float2 center = float2(0.5, 0.5);
                float dist = distance(i.uv, center);
                
                // Core
                float core = 1.0 - smoothstep(0.0, 0.3, dist);
                
                // Simple rocky texture noise could be here, but for now simple dot
                // Maybe irregular shape?
                
                // Alpha
                float alpha = core;
                if (alpha < 0.01) discard;
                
                // Apply brightness to color
                fixed4 col = _Color;
                col.rgb *= _Brightness;
                col.a = alpha;
                
                return col;
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}
