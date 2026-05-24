Shader "ARFaceFilter/VideoBackdropBackground"
{
    Properties
    {
        _MainTex ("Video", 2D) = "white" {}
        [Toggle] _FlipU ("Flip U (mirror)", Float) = 0
    }

    // Draws behind AR geometry so tracked face meshes / filters stay visible on top.
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Lighting Off
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
            float4 _MainTex_ST;
            float _FlipU;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float2 uv = TRANSFORM_TEX(v.uv, _MainTex);
                uv.x = lerp(uv.x, 1.0 - uv.x, _FlipU);
                o.uv = uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }

    FallBack Off
}
