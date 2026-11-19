Shader "Hidden/Koto/ScanInteractionDepth"
{
    SubShader
    {
        Cull Off
        ZWrite On
        ZTest LEqual
        ColorMask 0

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4x4 _ScanViewProjection;

            struct Attributes
            {
                float4 position : POSITION;
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                return mul(_ScanViewProjection, mul(unity_ObjectToWorld, input.position));
            }

            fixed4 frag() : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
