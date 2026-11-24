// What Texture Studio's Paint page puts on objects: a texture laid over the surface from all sides (no UVs needed), only
// where it was painted, with an uneven edge. Cut out, so it works with fog, shadows and depth effects.
Shader "HienaDev/Painted Layer"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Bumps", Range(0, 3)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.15
        _Tile ("Texture size (units)", Float) = 1
        _Edge ("Uneven edge", Range(0, 1)) = 0.6
        _EdgeSize ("Edge detail (units)", Float) = 0.25
        _Follow ("Follow the texture's shapes", Range(0, 1)) = 0.5
        _Dither ("Soft edge (dotted)", Range(0, 1)) = 0.25
        _TopOnly ("Only on what points up", Range(0, 1)) = 0
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal": "12.0" }
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HienaNoRepeat.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half _BumpScale, _Smoothness, _Edge, _Follow, _Dither, _TopOnly; float _Tile, _EdgeSize;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 brush : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 posScaled : TEXCOORD2; float3 normalOS : TEXCOORD3; float2 brush : TEXCOORD4; half4 fogVertex : TEXCOORD5; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

        Varyings Vert(Attributes v)
        {
            Varyings o = (Varyings)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz); o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = TransformObjectToWorldNormal(v.normalOS); o.normalOS = v.normalOS; o.brush = v.brush;
            // the texture sticks to the object, but isn't stretched when the object is
            float3 scale = float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22)); o.posScaled = v.positionOS.xyz * scale; o.normalOS = v.normalOS / max(scale, 1e-4);
            return o;
        }

        float3 Sides(float3 n) { float3 w = pow(abs(normalize(n)), 6.0); return w / (w.x + w.y + w.z); }
        half4 From3(TEXTURE2D_PARAM(t, s), float3 p, float3 w) { p /= max(0.001, _Tile); return SAMPLE_TEXTURE2D(t, s, p.zy) * w.x + SAMPLE_TEXTURE2D(t, s, p.xz) * w.y + SAMPLE_TEXTURE2D(t, s, p.xy) * w.z; }
        void Cut(Varyings i, float lum)
        {
            float d = length(i.brush); float3 q = i.posScaled / max(0.01, _EdgeSize); float3 w = Sides(i.normalOS); float n = HienaBlotches(q.zy) * w.x + HienaBlotches(q.xz) * w.y + HienaBlotches(q.xy) * w.z;
            float a = (1.0 - d) * 2.2 + (n - 0.5) * _Edge * 1.6 * saturate(d * 1.6) + (lum - 0.5) * _Follow * 0.8 * saturate(d * 1.6);
            a -= _TopOnly * saturate(0.75 - normalize(i.normalWS).y) * 3.0;
            float2 px = floor(i.positionCS.xy); float dots = frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715))));
            clip(a - lerp(0.35, dots * 1.2, _Dither * 0.8) );
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit" Tags { "LightMode" = "UniversalForwardOnly" } Offset -1, -1 ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VertLit
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            Varyings VertLit(Attributes v) { Varyings o = Vert(v); o.fogVertex = half4(ComputeFogFactor(o.positionCS.z), VertexLighting(o.positionWS, o.normalWS)); return o; }
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 w = Sides(i.normalOS); half4 c = From3(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), i.posScaled, w); Cut(i, dot(c.rgb, half3(0.299, 0.587, 0.114)));
                // bumps from each side
                float3 p = i.posScaled / max(0.001, _Tile); float3 no = normalize(i.normalOS); half3 nx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.zy), _BumpScale), ny = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.xz), _BumpScale), nz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, p.xy), _BumpScale);
                float3 bump = float3(0, nx.y, nx.x) * w.x + float3(ny.x, 0, ny.y) * w.y + float3(nz.x, nz.y, 0) * w.z; float3 nW = normalize(normalize(i.normalWS) + TransformObjectToWorldDir(bump, false) * 1.0);
                SurfaceData s = (SurfaceData)0; s.albedo = c.rgb * _BaseColor.rgb; s.alpha = 1; s.normalTS = half3(0, 0, 1); s.smoothness = _Smoothness; s.occlusion = 1;
                InputData d = (InputData)0; d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = nW; d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS); d.shadowCoord = TransformWorldToShadowCoord(i.positionWS); d.fogCoord = i.fogVertex.x; d.vertexLighting = i.fogVertex.yzw; d.bakedGI = SampleSH(nW); d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask = half4(1, 1, 1, 1);
                half4 col = UniversalFragmentPBR(d, s); col.rgb = MixFog(col.rgb, d.fogCoord); col.a = 1; return col;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode" = "DepthOnly" } Offset -1, -1 ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragD
            #pragma multi_compile_instancing
            half4 FragD(Varyings i) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); half4 c = From3(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), i.posScaled, Sides(i.normalOS)); Cut(i, dot(c.rgb, half3(0.299, 0.587, 0.114))); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode" = "DepthNormalsOnly" } Offset -1, -1 ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragN
            #pragma multi_compile_instancing
            half4 FragN(Varyings i) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); half4 c = From3(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), i.posScaled, Sides(i.normalOS)); Cut(i, dot(c.rgb, half3(0.299, 0.587, 0.114))); return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" } Offset -1, -1
        CGPROGRAM
        #pragma target 3.5
        #pragma surface surf Standard vertex:vert
        #define SAMPLE_TEXTURE2D_GRAD(t, s, u, x, y) tex2Dgrad(t, u, x, y)
        #include "HienaNoRepeat.hlsl"
        sampler2D _BaseMap; fixed4 _BaseColor; float _Smoothness, _Tile, _Edge, _EdgeSize, _Follow, _TopOnly;
        struct Input { float3 pos; float3 nOS; float3 worldNormal; float2 brush; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input, o); float3 sc = float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22)); o.pos = v.vertex.xyz * sc; o.nOS = v.normal / max(sc, 1e-4); o.brush = v.texcoord.xy; }
        void surf(Input i, inout SurfaceOutputStandard o)
        {
            float3 w = pow(abs(normalize(i.nOS)), 6.0); w /= w.x + w.y + w.z; float3 p = i.pos / max(0.001, _Tile); float4 c = tex2D(_BaseMap, p.zy) * w.x + tex2D(_BaseMap, p.xz) * w.y + tex2D(_BaseMap, p.xy) * w.z; float d = length(i.brush); float3 q = i.pos / max(0.01, _EdgeSize); float n = HienaBlotches(q.zy) * w.x + HienaBlotches(q.xz) * w.y + HienaBlotches(q.xy) * w.z;
            float a = (1.0 - d) * 2.2 + (n - 0.5) * _Edge * 1.6 * saturate(d * 1.6) + (dot(c.rgb, float3(0.299, 0.587, 0.114)) - 0.5) * _Follow * 0.8 * saturate(d * 1.6); a -= _TopOnly * saturate(0.75 - i.worldNormal.y) * 3.0; clip(a - 0.35); o.Albedo = c.rgb * _BaseColor.rgb; o.Smoothness = _Smoothness;
        }
        ENDCG
    }
}
