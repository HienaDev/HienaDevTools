// A lit surface whose texture never visibly repeats (see HienaNoRepeat.hlsl), with big light/dark areas and a second
// texture in patches. Works in URP (forward, forward+ and deferred projects) and in the Built-in pipeline.
Shader "HienaDev/Never Repeats Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Bumps", Range(0, 3)) = 1
        _MetallicGlossMap ("AO (G) + smoothness (A)", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0, 1)) = 1
        _OcclusionStrength ("Occlusion", Range(0, 1)) = 1
        [HDR] _EmissionColor ("Glow", Color) = (0, 0, 0, 1)
        _EmissionMap ("Glow map", 2D) = "white" {}
        [Header(Never repeats)]
        [Toggle] _Shuffle ("Shuffle patches", Float) = 1
        _PatchSize ("Patches per tile", Range(0.25, 4)) = 1
        _Turn ("Turn patches", Range(0, 1)) = 1
        _Snap ("Bricks across and down (0 = none)", Vector) = (0, 0, 0, 0)
        [Header(Big areas)]
        _Macro ("Light and dark areas", Range(0, 1)) = 0.25
        _MacroSize ("Area size (tiles)", Range(0.5, 20)) = 4
        _MacroColor ("Colour in the dark areas", Color) = (0.75, 0.7, 0.62, 1)
        [Header(Second texture in patches)]
        _SecondMap ("Second texture", 2D) = "white" {}
        _SecondAmount ("How much", Range(0, 1)) = 0
        _SecondSize ("Patch size (tiles)", Range(0.3, 20)) = 3
        _SecondSoft ("Soft edge", Range(0.02, 1)) = 0.25
        [HideInInspector] _Tiles ("Tiles", 2DArray) = "" {}
        [HideInInspector] _TilesNormal ("Tile normals", 2DArray) = "" {}
        [HideInInspector] _TilesMask ("Tile masks", 2DArray) = "" {}
        [HideInInspector] _TileCount ("Tile count", Float) = 1
        _TileRare ("How often the first tile shows", Range(0, 0.95)) = 0.3
        [Header(See through)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Cut out", Float) = 0
        _Cutoff ("Cut at", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    // ------------------------------------------------------------------ URP
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal": "12.0" }
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _SecondMap_ST, _Snap;
            half4 _BaseColor, _MacroColor, _EmissionColor;
            half _BumpScale, _Smoothness, _OcclusionStrength, _Shuffle, _Turn, _Macro, _SecondAmount, _SecondSoft, _Cutoff;
            float _PatchSize, _MacroSize, _SecondSize, _AlphaClip, _Cull, _TileCount, _TileRare;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _TILES
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HienaNoRepeat.hlsl"
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_SecondMap); SAMPLER(sampler_SecondMap);
            TEXTURE2D_ARRAY(_Tiles); SAMPLER(sampler_Tiles); TEXTURE2D_ARRAY(_TilesNormal); TEXTURE2D_ARRAY(_TilesMask);
            #define HIENA_TILE(tex, nr, n) SAMPLE_TEXTURE2D_ARRAY_GRAD(tex, sampler_Tiles, nr.uv##n, HienaTile(nr.pick[n], _TileCount, _TileRare), HienaTurn(nr.rot##n, nr.dx), HienaTurn(nr.rot##n, nr.dy))

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; float2 lm : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; float4 tangentWS : TEXCOORD3; half4 fogVertex : TEXCOORD4; float2 lm : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz); VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = n.normalWS; o.tangentWS = float4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.lm = v.lm * unity_LightmapST.xy + unity_LightmapST.zw;
                o.fogVertex = half4(ComputeFogFactor(p.positionCS.z), VertexLighting(p.positionWS, n.normalWS));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                HienaNR nr = HienaNoRepeat(i.uv, _PatchSize, _Turn, _Snap.xy);
                if (_Shuffle < 0.5) { nr.uv0 = i.uv; nr.rot0 = float4(1, 0, 0, 1); nr.w = float3(1, 0, 0); }
                half4 c;
                #if defined(_TILES)
                half4 t0 = HIENA_TILE(_Tiles, nr, 0), t1 = HIENA_TILE(_Tiles, nr, 1), t2 = HIENA_TILE(_Tiles, nr, 2);
                nr.w = HienaWeights(nr.w, float3(dot(t0.rgb, half3(0.299, 0.587, 0.114)), dot(t1.rgb, half3(0.299, 0.587, 0.114)), dot(t2.rgb, half3(0.299, 0.587, 0.114)))); c = t0 * nr.w.x + t1 * nr.w.y + t2 * nr.w.z;
                half3 n0 = HIENA_TILE(_TilesNormal, nr, 0).xyz * 2 - 1, n1 = HIENA_TILE(_TilesNormal, nr, 1).xyz * 2 - 1, n2 = HIENA_TILE(_TilesNormal, nr, 2).xyz * 2 - 1; n0.xy *= _BumpScale; n1.xy *= _BumpScale; n2.xy *= _BumpScale;
                #else
                HIENA_NR_COLOUR(c, _BaseMap, sampler_BaseMap, nr);
                half3 n0 = UnpackNormalScale(HIENA_NR_GRAD(_BumpMap, sampler_BumpMap, nr, 0), _BumpScale), n1 = UnpackNormalScale(HIENA_NR_GRAD(_BumpMap, sampler_BumpMap, nr, 1), _BumpScale), n2 = UnpackNormalScale(HIENA_NR_GRAD(_BumpMap, sampler_BumpMap, nr, 2), _BumpScale);
                #endif
                n0.xy = HienaTurnBack(nr.rot0, n0.xy); n1.xy = HienaTurnBack(nr.rot1, n1.xy); n2.xy = HienaTurnBack(nr.rot2, n2.xy);
                half3 nTS = normalize(n0 * nr.w.x + n1 * nr.w.y + n2 * nr.w.z);
                #if defined(_TILES)
                half4 m = HIENA_TILE(_TilesMask, nr, 0) * nr.w.x + HIENA_TILE(_TilesMask, nr, 1) * nr.w.y + HIENA_TILE(_TilesMask, nr, 2) * nr.w.z;
                #else
                half4 m = HIENA_NR_SAMPLE(_MetallicGlossMap, sampler_MetallicGlossMap, nr);
                #endif
                #if defined(_ALPHATEST_ON)
                clip(c.a * _BaseColor.a - _Cutoff);
                #endif
                // a second texture in patches: it settles in the dark parts first
                if (_SecondAmount > 0.001)
                {
                    HienaNR s = HienaNoRepeat(i.uv * _SecondMap_ST.xy, _PatchSize, _Turn, float2(0, 0));
                    half4 c2; HIENA_NR_COLOUR(c2, _SecondMap, sampler_SecondMap, s);
                    half k = HienaBlotches(i.uv / _SecondSize + 41.7) - (0.78 - _SecondAmount * 0.56) + (0.4 - dot(c.rgb, half3(0.299, 0.587, 0.114))) * 0.25;
                    c.rgb = lerp(c.rgb, c2.rgb, smoothstep(-_SecondSoft * 0.3, _SecondSoft * 0.3, k));
                }
                // big light and dark areas
                half macro = HienaBlotches(i.uv / _MacroSize);
                c.rgb *= lerp(half3(1, 1, 1), lerp(_MacroColor.rgb, half3(1.12, 1.12, 1.12), smoothstep(0.25, 0.75, macro)), _Macro);

                half3 glow = 0;
                #if defined(_EMISSION)
                glow = HIENA_NR_SAMPLE(_EmissionMap, sampler_EmissionMap, nr).rgb * _EmissionColor.rgb;
                #endif
                SurfaceData s = (SurfaceData)0; s.emission = glow;
                s.albedo = c.rgb * _BaseColor.rgb; s.alpha = 1; s.normalTS = nTS; s.metallic = m.r * 0; s.smoothness = m.a * _Smoothness; s.occlusion = lerp(1, m.g, _OcclusionStrength);
                InputData d = (InputData)0;
                float3 bit = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(nTS, half3x3(i.tangentWS.xyz, bit, i.normalWS)));
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS); d.shadowCoord = TransformWorldToShadowCoord(i.positionWS); d.fogCoord = i.fogVertex.x; d.vertexLighting = i.fogVertex.yzw;
                #if defined(LIGHTMAP_ON)
                d.bakedGI = SampleLightmap(i.lm, 0, d.normalWS);
                #else
                d.bakedGI = SampleSH(d.normalWS);
                #endif
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask = half4(1, 1, 1, 1);
                half4 col = UniversalFragmentPBR(d, s);
                col.rgb = MixFog(col.rgb, d.fogCoord); col.a = 1;
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    // ------------------------------------------------------------------ Built-in
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull [_Cull]
        CGPROGRAM
        #pragma target 3.5
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma shader_feature_local _ALPHATEST_ON
        #pragma shader_feature_local _EMISSION
        #define SAMPLE_TEXTURE2D_GRAD(t, s, u, x, y) tex2Dgrad(t, u, x, y)
        #include "HienaNoRepeat.hlsl"
        sampler2D _BaseMap, _BumpMap, _MetallicGlossMap, _SecondMap, _EmissionMap; float4 _EmissionColor;
        float4 _Snap, _SecondMap_ST; fixed4 _BaseColor, _MacroColor;
        float _BumpScale, _Smoothness, _OcclusionStrength, _Shuffle, _Turn, _Macro, _SecondAmount, _SecondSoft, _Cutoff, _PatchSize, _MacroSize, _SecondSize;
        struct Input { float2 uv_BaseMap; };
        void surf(Input i, inout SurfaceOutputStandard o)
        {
            float2 uv = i.uv_BaseMap;
            HienaNR nr = HienaNoRepeat(uv, _PatchSize, _Turn, _Snap.xy);
            if (_Shuffle < 0.5) { nr.uv0 = uv; nr.rot0 = float4(1, 0, 0, 1); nr.w = float3(1, 0, 0); }
            float4 c; HIENA_NR_COLOUR(c, _BaseMap, 0, nr);
            float3 n0 = UnpackScaleNormal(HIENA_NR_GRAD(_BumpMap, 0, nr, 0), _BumpScale), n1 = UnpackScaleNormal(HIENA_NR_GRAD(_BumpMap, 0, nr, 1), _BumpScale), n2 = UnpackScaleNormal(HIENA_NR_GRAD(_BumpMap, 0, nr, 2), _BumpScale);
            n0.xy = HienaTurnBack(nr.rot0, n0.xy); n1.xy = HienaTurnBack(nr.rot1, n1.xy); n2.xy = HienaTurnBack(nr.rot2, n2.xy);
            float4 m = HIENA_NR_SAMPLE(_MetallicGlossMap, 0, nr);
            #if defined(_ALPHATEST_ON)
            clip(c.a * _BaseColor.a - _Cutoff);
            #endif
            if (_SecondAmount > 0.001)
            {
                HienaNR s = HienaNoRepeat(uv * _SecondMap_ST.xy, _PatchSize, _Turn, float2(0, 0));
                float4 c2; HIENA_NR_COLOUR(c2, _SecondMap, 0, s);
                float k = HienaBlotches(uv / _SecondSize + 41.7) - (0.78 - _SecondAmount * 0.56) + (0.4 - dot(c.rgb, float3(0.299, 0.587, 0.114))) * 0.25;
                c.rgb = lerp(c.rgb, c2.rgb, smoothstep(-_SecondSoft * 0.3, _SecondSoft * 0.3, k));
            }
            float macro = HienaBlotches(uv / _MacroSize);
            c.rgb *= lerp(float3(1, 1, 1), lerp(_MacroColor.rgb, float3(1.12, 1.12, 1.12), smoothstep(0.25, 0.75, macro)), _Macro);
            #if defined(_EMISSION)
            o.Emission = HIENA_NR_SAMPLE(_EmissionMap, 0, nr).rgb * _EmissionColor.rgb;
            #endif
            o.Albedo = c.rgb * _BaseColor.rgb; o.Normal = normalize(n0 * nr.w.x + n1 * nr.w.y + n2 * nr.w.z); o.Smoothness = m.a * _Smoothness; o.Occlusion = lerp(1, m.g, _OcclusionStrength); o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
