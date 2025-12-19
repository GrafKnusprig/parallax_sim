Shader "Custom/StarPoint"
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float _Brightness;
            float _Size;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                // Transform vertex to world space
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                // Calculate billboard vectors for camera-facing quads
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                float3 upDir = float3(0, 1, 0);
                float3 rightDir = normalize(cross(upDir, viewDir));
                upDir = normalize(cross(viewDir, rightDir));
                
                // Scale based on distance and size parameter
                float dist = length(_WorldSpaceCameraPos - worldPos);
                float scale = _Size * max(0.1, dist * 0.0001);
                
                // Apply billboard transformation
                float3 localPos = v.vertex.xyz * scale;
                worldPos += rightDir * localPos.x + upDir * localPos.y;
                
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.uv = v.uv;
                o.worldPos = worldPos;
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Create a circular star point
                float2 center = float2(0.5, 0.5);
                float dist = distance(i.uv, center);
                
                // Smooth circular falloff
                float alpha = 1.0 - smoothstep(0.0, 0.5, dist);
                
                // Brighten the center
                alpha = pow(alpha, 0.8);
                
                // Distance-based brightness
                float brightness = _Brightness;
                
                fixed4 col = _Color;
                col.rgb *= brightness;
                col.a *= alpha;
                
                // Ensure minimum visibility
                col.a = max(col.a, 0.1 * alpha);
                
                return col;
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}