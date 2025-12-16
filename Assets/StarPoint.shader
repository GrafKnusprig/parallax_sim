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
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float distance : TEXCOORD1;
            };

            fixed4 _Color;
            float _Brightness;
            float _Size;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                
                // Calculate distance from camera for brightness falloff
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.distance = distance(_WorldSpaceCameraPos, worldPos);
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Create a circular star point
                float2 center = float2(0.5, 0.5);
                float dist = distance(i.uv, center);
                
                // Smooth circular falloff
                float alpha = 1.0 - smoothstep(0.0, 0.5, dist);
                
                // Brighten the center
                alpha = pow(alpha, 0.5);
                
                // Distance-based brightness (optional)
                float brightness = _Brightness;// / max(1.0, i.distance * 0.001);
                
                fixed4 col = _Color;
                col.rgb *= brightness;
                col.a *= alpha;
                
                return col;
            }
            ENDCG
        }
    }
    FallBack "Sprites/Default"
}