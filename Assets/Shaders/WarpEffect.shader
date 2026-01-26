Shader "Custom/WarpEffect"
{
    Properties
    {
        _MainTex ("Star Texture", 2D) = "black" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Speed ("Speed", Float) = 0.0
        _Alpha ("Alpha", Range(0,1)) = 0.0
        _Density ("Density", Float) = 1.0
        _StreakLength ("Streak Length", Float) = 2.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float3 viewDir : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Speed;
            float _Alpha;
            float _Density;
            float _StreakLength;

            // Pseudo-random function
            float rand(float2 co) {
                return frac(sin(dot(co.xy ,float2(12.9898,78.233))) * 43758.5453);
            }

            v2f vert (appdata v)
            {
                v2f o;
                
                // Wobble/Distortion based on speed to make it feel alive
                float4 pos = v.vertex;
                
                o.vertex = UnityObjectToClipPos(pos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                
                // View direction for fading at edges if needed (cylinder ends)
                o.viewDir = ObjSpaceViewDir(v.vertex);
                
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Scroll UVs over time based on speed
                // We scroll along V axis (longitudinal on cylinder)
                float2 uv = i.uv;
                uv.y += _Time.y * _Speed;
                
                // Generate procedural streaks if no texture
                // Or sample texture. Let's assume procedural for simplicity/robustness without assets.
                // We create a grid of cells
                
                float2 gridUV = uv * float2(_Density * 10.0, _Density); // More dense in X (around cylinder)
                float2 cellID = floor(gridUV);
                float2 cellUV = frac(gridUV);
                
                float randomVal = rand(cellID);
                
                // Randomize streak position within cell
                float streakPos = randomVal;
                
                // Threshold for star existence
                float isStar = step(0.95, randomVal); // Only 5% of cells have a star
                
                // Streak shape
                // We want them long in Y direction
                // Stretch the UV for the shape calc
                float2 shapeUV = cellUV;
                
                // Center gradient
                float distX = abs(shapeUV.x - 0.5) * 2.0;
                float distY = abs(shapeUV.y - 0.5) * 2.0;
                
                // Fade at edges of streak
                float streakShape = smoothstep(0.8, 0.0, distX);
                
                // Brightness variation
                float brightness = rand(cellID + 1.0) * 0.5 + 0.5;
                
                float4 col = _Color * brightness;
                col.a *= streakShape * isStar * _Alpha;
                
                // Fade out at the ends of the cylinder (UV.y) - actually because UVs scroll, 
                // we can't use UV.y for fixed fade. We need 'local' vertex position or original UV.
                // But for a tunnel effect, abrupt start/end is okay if it's long enough, 
                // or we use fog. Let's use simple distance fade from center of screen if possible,
                // or just rely on the transparency.
                
                return col;
            }
            ENDCG
        }
    }
}
