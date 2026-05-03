Shader "Custom/PencilBrushShader"
{
    Properties
    {
        _MainTex ("Canvas Texture", 2D) = "white" {} // 도화지 상태
        _BrushTex ("Pencil Texture", 2D) = "white" {} // ★ 우리가 만든 연필 텍스처
        _BrushUV ("Brush UV", Vector) = (0,0,0,0)
        _BrushColor ("Brush Color", Color) = (0, 0, 0, 1)
        _BrushSize ("Brush Size", Range(0.001, 0.1)) = 0.01
        _BrushHardness ("Brush Hardness", Range(0, 1)) = 0.5 
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

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _BrushTex; // 연필 텍스처 샘플러
            float4 _BrushUV;
            float4 _BrushColor;
            float _BrushSize;
            float _BrushHardness;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                // 1. 기존 도화지 색상
                fixed4 canvasCol = tex2D(_MainTex, i.uv);

                // 2. 현재 픽셀이 브러시 영역 안 어디에 위치하는지 계산 (0~1 범위로 변환)
                float2 brushUV = (i.uv - _BrushUV.xy) / _BrushSize + 0.5;

                // 3. 브러시 영역 안일 때만 텍스처를 읽어옴
                float paintFactor = 0;
                if (brushUV.x >= 0 && brushUV.x <= 1 && brushUV.y >= 0 && brushUV.y <= 1) {
                    // 연필 텍스처의 알파(A) 값을 가져와서 투명도 결정
                    paintFactor = tex2D(_BrushTex, brushUV).a;
                    
                    // 기존 Hardness 로직을 섞어 외곽을 더 흐릿하게 조절 가능
                    float dist = distance(i.uv, _BrushUV.xy);
                    float edgeSoftness = 1.0 - smoothstep(_BrushSize * _BrushHardness, _BrushSize, dist);
                    paintFactor *= edgeSoftness;
                }

                // 4. 연필 색상과 도화지 색상 혼합
                return lerp(canvasCol, _BrushColor, paintFactor * _BrushColor.a * 0.4);
            }
            ENDCG
        }
    }
}