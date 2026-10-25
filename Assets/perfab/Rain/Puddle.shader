Shader "Koto/Puddle"
{
    Properties
    {
        _PuddleColor ("Default Water Color", Color) = (0.3,0.48,0.58,0.6)
        _FlowSpeed ("Default Flow Speed", Float) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _PuddleColor;
                float _FlowSpeed;
            CBUFFER_END
            float _KotoPuddleControlActive;
            float4 _KotoPuddleColor;
            float _KotoPuddleSpeed;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            half4 Frag(Varyings input) : SV_Target
            {
                float control = step(0.5, _KotoPuddleControlActive);
                float4 tint = lerp(_PuddleColor, _KotoPuddleColor, control);
                float time = _Time.y * max(0.0, lerp(_FlowSpeed, _KotoPuddleSpeed, control));
                float2 p = input.uv * 2.0 - 1.0;
                float angle = atan2(p.y, p.x);
                float boundary = 0.82 + 0.07 * sin(angle * 3.0 + 0.6) + 0.04 * sin(angle * 7.0 - 1.1);
                float mask = 1.0 - smoothstep(boundary - 0.07, boundary, length(p));
                clip(mask - 0.001);

                // Unity Plane 為 10×10 單位。依世界尺寸計算距離，非等比縮放仍保留圓形漣漪。
                float2 size = max(float2(length(TransformObjectToWorldDir(float3(1,0,0), false)),
                    length(TransformObjectToWorldDir(float3(0,0,1), false))) * 10.0, 0.001);
                float radiusMax = min(size.x, size.y) * 0.19;
                float width = max(0.008, radiusMax * 0.045);
                float seed = Hash(TransformObjectToWorld(float3(0,0,0)).xz);
                float rings = 0.0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float cycle = time * 0.65 + i * 0.27 + seed;
                    float age = frac(cycle);
                    float2 key = float2(floor(cycle) + i * 13.7, seed * 91.3);
                    float2 center = 0.27 + 0.46 * float2(Hash(key), Hash(key + 19.2));
                    float distanceToCenter = length((input.uv - center) * size);
                    float ring = 1.0 - smoothstep(width * 0.3, width, abs(distanceToCenter - age * radiusMax));
                    rings += ring * smoothstep(0.0, 0.12, age) * (1.0 - age);
                }
                float wave = sin(input.positionWS.x * 4.1 + input.positionWS.z * 2.7 + time * 1.1)
                    * sin(input.positionWS.z * 3.2 - time * 0.8);
                float glint = pow(saturate(wave * 0.5 + 0.5), 10.0) * 0.12;
                float3 color = tint.rgb * (0.9 + wave * 0.08);
                color = lerp(color, float3(0.8,0.9,1.0), saturate(rings * 0.42 + glint));
                return half4(color, mask * tint.a);
            }
            ENDHLSL
        }
    }
}
