Shader "Koto/ScanInteractionOverlay"
{
    Properties
    {
        _MainTex ("UI Texture", 2D) = "white" {}
        _InteractionMask ("Interaction Mask", 2D) = "black" {}
        _OutlineColor ("Outline", Color) = (1,1,1,0.35)
        _OutlineWidth ("Width", Float) = 2
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _InteractionMask;
            float4 _InteractionMask_TexelSize, _OutlineColor;
            float _OutlineWidth;
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                // 全螢幕 Image 的 UV 與輪廓貼圖一一對應，不混用 UI 攝影機的投影參數。
                float2 uv=i.uv;
                float2 center=tex2D(_InteractionMask,uv).rg;
                float edge=0;
                for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
                    edge=max(edge,tex2D(_InteractionMask,uv+float2(x,y)*_InteractionMask_TexelSize.xy*_OutlineWidth).g);
                edge=saturate(edge-center.r)*_OutlineColor.a;
                float dark=i.color.a*(1-center.r);
                float alpha=edge+dark*(1-edge);
                float3 rgb=(_OutlineColor.rgb*edge+i.color.rgb*dark*(1-edge))/max(alpha,0.0001);
                return float4(rgb,alpha);
            }
            ENDCG
        }
    }
}
