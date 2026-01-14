Shader "Custom/EnhancedPlanet"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,2)) = 1.0
        _SpecGlossMap ("Specular Map", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossMapScale ("Specular Scale", Range(0,1)) = 1.0
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        
        // Fake Sun Lighting
        _SunPosition ("Sun Position (World Space)", Vector) = (0,0,0,1)
        _SunColor ("Sun Color", Color) = (1,1,1,1)
        _SunIntensity ("Sun Intensity", Range(0,5)) = 1.0
        _AmbientLight ("Ambient Light", Range(0,1)) = 0.1
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf CustomLighting fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _SpecGlossMap;
        sampler2D _EmissionMap;
        
        half _BumpScale;
        half _Glossiness;
        half _GlossMapScale;
        half _Metallic;
        fixed4 _EmissionColor;
        
        // Fake Sun Lighting
        float3 _SunPosition;
        fixed4 _SunColor;
        half _SunIntensity;
        half _AmbientLight;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal; INTERNAL_DATA
        };
        
        // Custom lighting function to fake sun illumination
        half4 LightingCustomLighting(SurfaceOutputStandard s, half3 viewDir, UnityGI gi)
        {
            // This will be calculated in the surface function
            return half4(s.Emission, s.Alpha);
        }
        
        void LightingCustomLighting_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGlobalIllumination(data, 1.0, s.Normal);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Albedo
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = c.rgb;
            
            // Normal mapping
            float3 normalMap = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            normalMap = normalize(float3(normalMap.xy * _BumpScale, normalMap.z));
            o.Normal = normalMap;
            
            // Get world-space normal
            float3 worldNormal = WorldNormalVector(IN, o.Normal);
            
            // Calculate direction from planet surface to sun
            float3 sunDirection = normalize(_SunPosition - IN.worldPos);
            
            // Calculate diffuse lighting (Lambertian)
            half NdotL = max(0, dot(worldNormal, sunDirection));
            
            // Calculate specular (Blinn-Phong)
            float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);
            float3 halfDir = normalize(sunDirection + viewDir);
            half NdotH = max(0, dot(worldNormal, halfDir));
            
            // Specular map and smoothness
            fixed4 specular = tex2D(_SpecGlossMap, IN.uv_MainTex);
            half smoothness = specular.a * _Glossiness * _GlossMapScale;
            half specPower = exp2(smoothness * 10.0 + 1.0);
            half spec = pow(NdotH, specPower) * smoothness;
            
            // Combine diffuse and specular with sun color
            fixed3 diffuseLight = NdotL * _SunColor.rgb * _SunIntensity;
            fixed3 specularLight = spec * _SunColor.rgb * _SunIntensity * specular.rgb * _GlossMapScale;
            
            // Add ambient light (so dark side isn't completely black)
            fixed3 ambientLight = _AmbientLight * o.Albedo;
            
            // Apply lighting to albedo
            fixed3 litColor = o.Albedo * (diffuseLight + ambientLight) + specularLight;
            
            // Emission (for night lights) - only show on dark side
            fixed3 nightLights = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb;
            // Fade out night lights on the lit side
            half nightLightMask = saturate(1.0 - NdotL * 2.0);
            litColor += nightLights * nightLightMask;
            
            // Store final color in emission so it bypasses the default lighting
            o.Emission = litColor;
            
            // Zero out other outputs since we're handling everything ourselves
            o.Albedo = 0;
            o.Metallic = 0;
            o.Smoothness = 0;
            o.Alpha = c.a;
        }
        ENDCG
    }
    
    FallBack "Standard"
}
