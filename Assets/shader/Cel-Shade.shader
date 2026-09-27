Shader "Custom/Cel-Shade"
{
    Properties
    {
        _BaseColor ("Base Color Tint", Color) = (1,1,1,1)
        _Threshold ("Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowBright ("Shadow Brightness", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Threshold;
                float _ShadowBright;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                float NdotL = dot(normalize(IN.normalWS), normalize(mainLight.direction));
                float u = saturate(NdotL);
                
                float shade = (u > _Threshold) ? 1.0 : _ShadowBright;
                
                float3 finalRGB = _BaseColor.rgb * mainLight.color * shade;
                
                return float4(finalRGB, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}