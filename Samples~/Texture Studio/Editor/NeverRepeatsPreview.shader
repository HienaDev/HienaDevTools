// Draws a texture flat in the Texture Studio window the way "HienaDev/Never Repeats Lit" lays it out. Works in every pipeline.
Shader "Hidden/HienaDev/NeverRepeatsPreview"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} _Tiles ("Tiles", 2DArray) = "" {} _SecondMap ("Second", 2D) = "white" {} }
    SubShader
    {
        Tags { "ForceSupported" = "True" }
        Cull Off ZWrite Off ZTest Always Blend One Zero
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma multi_compile_local _ _TILES
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #define SAMPLE_TEXTURE2D_GRAD(t, s, u, x, y) tex2Dgrad(t, u, x, y)
            #include "../Runtime/Shaders/HienaNoRepeat.hlsl"
            sampler2D _MainTex, _SecondMap; UNITY_DECLARE_TEX2DARRAY(_Tiles); float _TileCount, _TileRare; float4 _Snap, _MacroColor;
            float _Repeat, _RepeatY, _PatchSize, _Turn, _Macro, _MacroSize, _SecondAmount, _SecondSize, _SecondSoft;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv * float2(_Repeat, _RepeatY);
                HienaNR nr = HienaNoRepeat(uv, _PatchSize, _Turn, _Snap.xy);
                float4 c;
                #if defined(_TILES)
                float4 c0 = _Tiles.SampleGrad(sampler_Tiles, float3(nr.uv0, HienaTile(nr.pick.x, _TileCount, _TileRare)), HienaTurn(nr.rot0, nr.dx), HienaTurn(nr.rot0, nr.dy)), c1 = _Tiles.SampleGrad(sampler_Tiles, float3(nr.uv1, HienaTile(nr.pick.y, _TileCount, _TileRare)), HienaTurn(nr.rot1, nr.dx), HienaTurn(nr.rot1, nr.dy)), c22 = _Tiles.SampleGrad(sampler_Tiles, float3(nr.uv2, HienaTile(nr.pick.z, _TileCount, _TileRare)), HienaTurn(nr.rot2, nr.dx), HienaTurn(nr.rot2, nr.dy));
                float3 hl = float3(dot(c0.rgb, float3(0.299, 0.587, 0.114)), dot(c1.rgb, float3(0.299, 0.587, 0.114)), dot(c22.rgb, float3(0.299, 0.587, 0.114))); nr.w = HienaWeights(nr.w, hl); c = c0 * nr.w.x + c1 * nr.w.y + c22 * nr.w.z;
                #else
                HIENA_NR_COLOUR(c, _MainTex, 0, nr);
                #endif
                if (_SecondAmount > 0.001)
                {
                    HienaNR s = HienaNoRepeat(uv, _PatchSize, _Turn, float2(0, 0));
                    float4 c2; HIENA_NR_COLOUR(c2, _SecondMap, 0, s);
                    float k = HienaBlotches(uv / _SecondSize + 41.7) - (0.78 - _SecondAmount * 0.56) + (0.4 - dot(c.rgb, float3(0.299, 0.587, 0.114))) * 0.25;
                    c.rgb = lerp(c.rgb, c2.rgb, smoothstep(-_SecondSoft * 0.3, _SecondSoft * 0.3, k));
                }
                float macro = HienaBlotches(uv / _MacroSize);
                c.rgb *= lerp(float3(1, 1, 1), lerp(_MacroColor.rgb, float3(1.12, 1.12, 1.12), smoothstep(0.25, 0.75, macro)), _Macro);
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}
