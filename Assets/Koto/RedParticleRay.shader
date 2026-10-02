Shader "UndeterminedColor/RedParticleRay"
{
    Properties
    {
        _RayColor ("Ray Color", Color) = (1,0.015,0.005,1)
        _Brightness ("Brightness", Float) = 2
        _GlowWidth ("Glow Width", Float) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _RayColor;
                float _Brightness;
                float _GlowWidth;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float distance = abs(input.uv.y * 2 - 1);
                float core = 1 - smoothstep(0.1, 1.0, distance * _GlowWidth);
                float glow = pow(saturate(1 - distance), 3) * 0.35;
                float endFade = smoothstep(0, 0.02, input.uv.x) * smoothstep(0, 0.02, 1 - input.uv.x);
                return half4(_RayColor.rgb * _Brightness, saturate(core + glow) * endFade * _RayColor.a);
            }
            ENDHLSL
        }
    }
}
