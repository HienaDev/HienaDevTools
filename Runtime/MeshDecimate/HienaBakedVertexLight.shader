// Shows light baked into vertex colours (Mesh Kit > PSX > Bake light): texture x colour x vertex colour, no real-time
// lighting, so it costs almost nothing. Works in URP and the Built-in pipeline.
Shader "HienaDev/Baked Vertex Light"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Range(0, 4)) = 1
        [Toggle(_AFFINE)] _Affine ("PS1 texture warp", Float) = 0
        _Cutoff ("Cut out below", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _AFFINE
            #include "UnityCG.cginc"

            sampler2D _BaseMap; float4 _BaseMap_ST; float4 _BaseColor; float _Brightness; float _Cutoff;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uv : TEXCOORD0; // (uv * w, w) when warping: dividing after the GPU's interpolation undoes its perspective fix
                float4 color : COLOR;
                UNITY_FOG_COORDS(1)
            };

            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex);
                float2 uv = TRANSFORM_TEX(v.uv, _BaseMap);
            #if _AFFINE
                o.uv = float3(uv * o.pos.w, o.pos.w); // the PS1 had no perspective correction
            #else
                o.uv = float3(uv, 1);
            #endif
                o.color = v.color; UNITY_TRANSFER_FOG(o, o.pos); return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_BaseMap, i.uv.xy / i.uv.z) * _BaseColor;
                clip(c.a - _Cutoff);
                c.rgb *= i.color.rgb * _Brightness;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
