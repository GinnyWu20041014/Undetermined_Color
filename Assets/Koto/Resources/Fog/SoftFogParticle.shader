Shader "Universal Render Pipeline/Particles/Soft Fog"
{
    Properties
    {
        [MainColor] _BaseColor("Fog Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _PlayerPosition0("Player Position 0", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PlayerPosition1("Player Position 1", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PlayerPosition2("Player Position 2", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PlayerPosition3("Player Position 3", Vector) = (0, 0, 0, 0)
        [HideInInspector] _PlayerCount("Player Count", Float) = 0
        [HideInInspector] _PlayerClearRadius("Player Clear Radius", Float) = 1.8
        [HideInInspector] _PlayerClearFeather("Player Clear Feather", Float) = 1.1
        [HideInInspector] _FogRange("Fog Range", Vector) = (18, 2, 8, 0)
        [HideInInspector] _FogEdgeFeather("Fog Edge Feather", Float) = 0.96
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FogUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _PlayerPosition0;
                float4 _PlayerPosition1;
                float4 _PlayerPosition2;
                float4 _PlayerPosition3;
                float _PlayerCount;
                float _PlayerClearRadius;
                float _PlayerClearFeather;
                float4 _FogRange;
                float _FogEdgeFeather;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _BaseColor;
                output.uv = input.uv;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centeredUV = (input.uv - 0.5) * 2.0;
                float radiusSquared = dot(centeredUV, centeredUV);
                half softEdge = exp(-radiusSquared * 2.8);

                // Keep a soft circular pocket around each player, in the world ground plane.
                float playerDistance = 1e6;
                if (_PlayerCount > 0.5) playerDistance = min(playerDistance, distance(input.positionWS.xz, _PlayerPosition0.xz));
                if (_PlayerCount > 1.5) playerDistance = min(playerDistance, distance(input.positionWS.xz, _PlayerPosition1.xz));
                if (_PlayerCount > 2.5) playerDistance = min(playerDistance, distance(input.positionWS.xz, _PlayerPosition2.xz));
                if (_PlayerCount > 3.5) playerDistance = min(playerDistance, distance(input.positionWS.xz, _PlayerPosition3.xz));
                half playerFade = smoothstep(_PlayerClearRadius, _PlayerClearRadius + max(_PlayerClearFeather, 0.01), playerDistance);

                return half4(input.color.rgb, saturate(input.color.a * softEdge * playerFade * 1.35));
            }
            ENDHLSL
        }
    }
}
