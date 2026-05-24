Shader "ARFaceFilter/FaceMaskTransparent"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint & Alpha", Color) = (1,1,1,1)
        [Toggle] _FlipU ("Flip U (mirror with webcam)", Float) = 0
        [Toggle] _KeyBlackToAlpha ("Key black to alpha (JPEG / RGB)", Float) = 0
        _BlackCutoff ("Black key cutoff", Range (0.0, 0.99)) = 0.045
        _BlackFeather ("Black key soften", Range (0.001, 0.5)) = 0.035
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 150
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                fixed4 vc : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _FlipU;
            half _KeyBlackToAlpha;
            half _BlackCutoff;
            half _BlackFeather;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float2 uv = TRANSFORM_TEX(v.uv, _MainTex);
                uv.x = lerp(uv.x, 1.0 - uv.x, _FlipU);
                o.uv = uv;
                o.vc = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                half maxRgb = max(max(tex.r, tex.g), tex.b);
                half keyMul = 1;
                UNITY_BRANCH
                if (_KeyBlackToAlpha >= 0.5)
                {
                    keyMul = saturate((maxRgb - _BlackCutoff) / max(_BlackFeather, 1e-3));
                }
                fixed4 col = tex * i.vc;
                col.a *= keyMul;
                return col;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
