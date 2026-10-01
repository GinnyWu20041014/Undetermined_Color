Shader "Koto/ScanInteractionSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _MaskColor ("Mask", Color) = (1,1,1,1)
        _Opacity ("Opacity", Float) = 1
        _Flip ("Flip", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One One
        BlendOp Max
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _ScanTexture;
        float4 _MaskColor;
        float _Opacity;
        float2 _Flip;
        float4x4 _ScanViewProjection;
        float4 _ScanTextureST;
        struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
        v2f vert(appdata_base v) { v2f o; o.pos=mul(_ScanViewProjection,mul(unity_ObjectToWorld,v.vertex)); o.uv=v.texcoord; return o; }
        v2f spriteVert(appdata_base v) { v.vertex.xy *= _Flip; return vert(v); }
        fixed4 spriteFrag(v2f i):SV_Target { clip(tex2D(_MainTex,i.uv).a * _Opacity - 0.05); return _MaskColor; }
        fixed4 meshFrag(v2f i):SV_Target { clip(tex2D(_ScanTexture,i.uv * _ScanTextureST.xy + _ScanTextureST.zw).a - 0.05); return _MaskColor; }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex spriteVert
            #pragma fragment spriteFrag
            ENDCG
        }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment meshFrag
            ENDCG
        }
    }
}
