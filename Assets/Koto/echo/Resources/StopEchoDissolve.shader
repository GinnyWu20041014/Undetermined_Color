Shader "Koto/StopEchoDissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Progress ("Dissolve Progress", Range(0, 1)) = 0
        _VerticalBounds ("Local Bottom and Height", Vector) = (0, 1, 0, 0)
        _EdgeColor ("Dissolve Edge", Color) = (0.65, 0.9, 1, 1)
        _ParticleMode ("Particle Mode", Float) = 0
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Flip ("Flip", Vector) = (1, 1, 1, 1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" "DisableBatching"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            float _Progress;
            float4 _VerticalBounds;
            float4 _EdgeColor;
            float _ParticleMode;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float height : TEXCOORD1;
                float4 color : COLOR;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                if (_ParticleMode < 0.5)
                {
                    input.vertex = UnityFlipSprite(input.vertex.xyz, _Flip);
                    input.color *= _RendererColor * _Color;
                }
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.height = (input.vertex.y - _VerticalBounds.x) / _VerticalBounds.y;
                output.color = input.color;
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                if (_ParticleMode > 0.5)
                {
                    float radius = length(input.uv * 2 - 1);
                    return float4(input.color.rgb, input.color.a * (1 - smoothstep(0.2, 1, radius)));
                }
                float4 color = SampleSpriteTexture(input.uv) * input.color;
                // 不規則的細碎邊緣隨進度由下往上推進。
                float2 cell = floor(input.uv * 180);
                float noise = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                float distance = input.height + noise * 0.06 - lerp(-0.02, 1.08, _Progress);
                clip(distance);
                clip(color.a - 0.001);
                float edge = (1 - smoothstep(0, 0.045, distance)) * step(0.001, _Progress);
                color.rgb = lerp(color.rgb, _EdgeColor.rgb, edge);
                return color;
            }
            ENDHLSL
        }
    }
}
