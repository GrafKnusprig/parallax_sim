Shader "Custom/EnhancedPlanet"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Unlit
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
        };
        
        half4 LightingUnlit(SurfaceOutput s, half3 lightDir, half atten)
        {
            return half4(s.Emission, s.Alpha);
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            o.Emission = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    
    FallBack "Diffuse"
}
