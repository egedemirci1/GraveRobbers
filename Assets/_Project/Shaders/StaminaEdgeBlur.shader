Shader "GraveRobbers/StaminaEdgeBlur"
{
    Properties
    {
        _BlurIntensity ("Blur Intensity", Range(0, 1)) = 0
        _InnerRadius ("Inner Radius", Range(0, 1)) = 0.62
        _OuterRadius ("Outer Radius", Range(0.2, 1.2)) = 1
        _SampleRadius ("Sample Radius", Range(0, 0.02)) = 0.008
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "StaminaEdgeBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _BlurIntensity;
                float _InnerRadius;
                float _OuterRadius;
                float _SampleRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float intensity = saturate(_BlurIntensity);
                float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);

                float aspect = _ScaledScreenParams.x * rcp(max(_ScaledScreenParams.y, 1));
                float2 fromCenter = (screenUv - 0.5) * float2(aspect, 1);
                float corner = length(float2(aspect * 0.5, 0.5));
                float edge = saturate(length(fromCenter) / max(corner, 1e-4));
                float mask = smoothstep(_InnerRadius, max(_OuterRadius, _InnerRadius + 0.01), edge);
                float amount = mask * intensity;

                if (amount <= 0.001)
                    return half4(0, 0, 0, 0);

                float radius = _SampleRadius * amount;
                float2 texel = float2(radius / max(aspect, 1e-4), radius);
                half3 blurred = 0;

                [unroll]
                for (int ring = 0; ring < 2; ring++)
                {
                    float ringScale = ring == 0 ? 0.5 : 1;
                    [unroll]
                    for (int tap = 0; tap < 6; tap++)
                    {
                        float angle = (tap + ring * 0.5) * (6.2831853 / 6);
                        float2 dir = float2(cos(angle), sin(angle));
                        blurred += (half3)SampleSceneColor(screenUv + dir * texel * ringScale);
                    }
                }

                blurred *= 0.0833333;
                return half4(blurred, amount);
            }
            ENDHLSL
        }
    }
}
