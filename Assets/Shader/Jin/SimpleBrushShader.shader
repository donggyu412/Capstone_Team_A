Shader "Custom/SimpleBrushShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {} // CanvasPainter가 자동으로 이전 도화지 상태를 넣어줍니다.
        _BrushUV ("Brush UV", Vector) = (0,0,0,0) // C# 스크립트에서 넘겨줄 마우스 위치
        _BrushColor ("Brush Color", Color) = (0, 0, 0, 1) // 물감 색상 (기본: 검정)
        _BrushSize ("Brush Size", Range(0.001, 0.1)) = 0.01 // 붓 크기
        _BrushHardness ("Brush Hardness", Range(0, 1)) = 0.5 // 붓 끝의 부드러운 정도
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _BrushUV;
            float4 _BrushColor;
            float _BrushSize;
            float _BrushHardness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 1. 도화지의 원래 색상을 가져옵니다.
                fixed4 col = tex2D(_MainTex, i.uv);

                // 2. 현재 픽셀과 마우스가 클릭한 위치(_BrushUV) 사이의 거리를 구합니다.
                float dist = distance(i.uv, _BrushUV.xy);

                // 3. 거리에 따라 색상을 칠할지 말지 결정합니다. (부드러운 경계선 처리)
                float paintFactor = 1.0 - smoothstep(_BrushSize * _BrushHardness, _BrushSize, dist);

                // 4. 원래 도화지 색상과 물감 색상을 자연스럽게 섞어줍니다.
                return lerp(col, _BrushColor, paintFactor * _BrushColor.a);
            }
            ENDCG
        }
    }
}