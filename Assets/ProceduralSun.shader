Shader "Custom/ProceduralSun"
{
    Properties
    {
        _BaseColor      ("Base Color", Color) = (1,0.3,0,1)
        _EmissionColor  ("Emission Color", Color) = (1,0.9,0.4,1)
        _NoiseScale     ("Noise Scale", Float) = 2.0
        _NoiseIntensity ("Noise Intensity", Float) = 3.0
        _FlowSpeed1     ("Flow Speed 1", Float) = 0.3
        _FlowSpeed2     ("Flow Speed 2", Float) = 0.7
        _RimPower       ("Rim Power", Float) = 3.0
        _RimIntensity   ("Rim Intensity", Float) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos    : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EmissionColor;
            float  _NoiseScale;
            float  _NoiseIntensity;
            float  _FlowSpeed1;
            float  _FlowSpeed2;
            float  _RimPower;
            float  _RimIntensity;
            CBUFFER_END

            // --------------------
            // Cheap 3D noise
            // --------------------

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = hash(i + float3(0,0,0));
                float n100 = hash(i + float3(1,0,0));
                float n010 = hash(i + float3(0,1,0));
                float n110 = hash(i + float3(1,1,0));
                float n001 = hash(i + float3(0,0,1));
                float n101 = hash(i + float3(1,0,1));
                float n011 = hash(i + float3(0,1,1));
                float n111 = hash(i + float3(1,1,1));

                float n00 = lerp(n000, n100, u.x);
                float n10 = lerp(n010, n110, u.x);
                float n01 = lerp(n001, n101, u.x);
                float n11 = lerp(n011, n111, u.x);

                float n0 = lerp(n00, n10, u.y);
                float n1 = lerp(n01, n11, u.y);

                return lerp(n0, n1, u.z);
            }

            float fbm(float3 p)
            {
                float v = 0;
                float a = 0.5;

                for (int i = 0; i < 5; i++)
                {
                    v += a * noise3D(p);
                    p *= 2.0;
                    a *= 0.5;
                }
                return v;
            }

            // --------------------
            // Vertex
            // --------------------

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                OUT.worldPos    = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionHCS = TransformWorldToHClip(OUT.worldPos);

                return OUT;
            }

            // --------------------
            // Fragment
            // --------------------

            half4 frag (Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.worldNormal);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - IN.worldPos);

                float t = _Time.y;

                float3 p = IN.worldPos * _NoiseScale;

                float n1 = fbm(p + float3(t * _FlowSpeed1, 0, 0));
                float n2 = fbm(p + float3(0, t * _FlowSpeed2, t * 0.3));

                float n = saturate(n1 * 0.6 + n2 * 0.4);
                n = pow(n, 2.0);

                float3 hot    = _EmissionColor.rgb;
                float3 cooler = _BaseColor.rgb * 0.5;

                float3 surfaceColor = lerp(cooler, hot, n);

                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower);
                float rimGlow = rim * _RimIntensity;

                float3 emission = surfaceColor * (_NoiseIntensity * n + rimGlow);

                return half4(emission, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}