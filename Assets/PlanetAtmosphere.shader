Shader "Custom/PlanetAtmosphere"
{
    Properties
    {
        _MainTex ("Cloud/Atmosphere Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,0.5)
        _Opacity ("Opacity", Range(0,1)) = 0.5
        _FresnelPower ("Fresnel Power", Range(0,10)) = 3.0
        _FresnelColor ("Fresnel Color", Color) = (0.5, 0.7, 1, 1)
    }
    
    SubShader
    {
        Tags { 
            "Queue"="Transparent" 
            "RenderType"="Transparent" 
            "IgnoreProjector"="True"
        }
        LOD 200
        
        // Render after opaque objects
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Back

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Opacity;
        half _FresnelPower;
        fixed4 _FresnelColor;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Sample cloud/atmosphere texture
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            
            // Calculate fresnel effect for atmospheric glow
            float fresnel = 1.0 - saturate(dot(normalize(IN.viewDir), WorldNormalVector(IN, o.Normal)));
            fresnel = pow(fresnel, _FresnelPower);
            
            // Combine texture color with fresnel glow
            fixed3 atmosphereColor = lerp(c.rgb, _FresnelColor.rgb, fresnel * 0.5);
            
            o.Albedo = atmosphereColor;
            o.Alpha = c.a * _Opacity;
            o.Smoothness = 0.0;
            o.Metallic = 0.0;
            
            // Add slight emission for glow effect
            o.Emission = _FresnelColor.rgb * fresnel * 0.3;
        }
        ENDCG
    }
    
    FallBack "Transparent/Diffuse"
}
