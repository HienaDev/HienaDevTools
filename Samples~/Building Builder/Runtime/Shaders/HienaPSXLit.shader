// The old-console look for the Building Builder (and anything else): vertices that snap to a coarse grid on screen
// (the wobble), textures that aren't corrected for perspective (the swim), hard pixels, and light worked out per vertex.
Shader "HienaDev/PSX Lit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Wobble ("Wobble", Range(0, 1)) = 0.5
        _Swim ("Texture swim", Range(0, 1)) = 0.35
        _Colours ("Fewer colours", Range(0, 1)) = 0.5
        _Cutoff ("Cut out below", Range(0, 1)) = 0.5
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal": "12.0" }
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SamplerState sampler_point_repeat;
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half _Wobble, _Swim, _Cutoff, _Colours;
        CBUFFER_END
        float4 Snap(float4 cs)
        {
            // the old screen had about 320 pixels across: corners could only land on those. 0 = off, 0.5 = 320, 1 = 120
            if (_Wobble <= 0.001) return cs; float res = exp2(lerp(log2(1280.0), log2(120.0), _Wobble)); float2 grid = float2(res, res * _ScreenParams.y / _ScreenParams.x) * 0.5;
            cs.xy = round(cs.xy / cs.w * grid) / grid * cs.w; return cs;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit" Tags { "LightMode" = "UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 uvw : TEXCOORD0; float2 uv : TEXCOORD3; half3 light : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A v)
            {
                V o = (V)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz); float3 n = TransformObjectToWorldNormal(v.normalOS); float4 cs = Snap(TransformWorldToHClip(ws)); o.positionCS = cs;
                float2 uv = TRANSFORM_TEX(v.uv, _BaseMap); o.uvw = float3(uv * cs.w, cs.w); o.uv = uv;
                // light per corner, like the old hardware
                Light l = GetMainLight(TransformWorldToShadowCoord(ws)); half3 c = SampleSH(n) + l.color * saturate(dot(n, l.direction)) * l.shadowAttenuation;
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                c += VertexLighting(ws, n);
                #endif
                o.light = c; o.fog = ComputeFogFactor(cs.z); return o;
            }
            half4 Frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); float2 affine = i.uvw.xy / i.uvw.z; float2 uv = lerp(i.uv, affine, _Swim);
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_point_repeat, uv) * _BaseColor; clip(t.a - _Cutoff * step(0.001, _Cutoff) * 0.999);
                half3 c = t.rgb * i.light; c = MixFog(c, i.fog);
                // fewer colours (15 bits at 0.5, very few at 1), with a 4x4 dot pattern between them
                if (_Colours > 0.001) { float levels = exp2(lerp(8.0, 2.0, _Colours)); uint2 p = uint2(i.positionCS.xy) & 3; const float b[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 }; float d = (b[p.y * 4 + p.x] + 0.5) / 16.0 - 0.5; c = floor(saturate(c) * (levels - 1) + 0.5 + d) / (levels - 1); }
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode" = "ShadowCaster" } ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex VertS
            #pragma fragment FragS
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 VertS(A v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); float3 ws = TransformObjectToWorld(v.positionOS.xyz); float3 n = TransformObjectToWorldNormal(v.normalOS); float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs; }
            half4 FragS() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode" = "DepthOnly" } ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex VertD
            #pragma fragment FragD
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 VertD(A v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return Snap(TransformObjectToHClip(v.positionOS.xyz)); }
            half4 FragD() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode" = "DepthNormalsOnly" } ZWrite On
            HLSLPROGRAM
            #pragma vertex VertN
            #pragma fragment FragN
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; };
            V VertN(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = Snap(TransformObjectToHClip(v.positionOS.xyz)); o.n = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 FragN(V i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _BaseMap; float4 _BaseMap_ST; fixed4 _BaseColor; half _Wobble, _Swim, _Colours;
            struct v2f { float4 pos : SV_POSITION; float3 uvw : TEXCOORD0; float2 uv : TEXCOORD3; fixed3 light : TEXCOORD1; UNITY_FOG_COORDS(2) };
            v2f vert(appdata_base v)
            {
                v2f o; float4 cs = UnityObjectToClipPos(v.vertex); float res = exp2(lerp(log2(1280.0), log2(120.0), _Wobble)); float2 grid = float2(res, res * _ScreenParams.y / _ScreenParams.x) * 0.5; if (_Wobble > 0.001) cs.xy = round(cs.xy / cs.w * grid) / grid * cs.w; o.pos = cs;
                float2 uv = TRANSFORM_TEX(v.texcoord, _BaseMap); o.uvw = float3(uv * cs.w, cs.w); o.uv = uv; float3 n = UnityObjectToWorldNormal(v.normal); o.light = ShadeSH9(float4(n, 1)) + _LightColor0.rgb * saturate(dot(n, _WorldSpaceLightPos0.xyz)); UNITY_TRANSFER_FOG(o, o.pos); return o;
            }
            fixed4 frag(v2f i) : SV_Target { float2 uv = lerp(i.uv, i.uvw.xy / i.uvw.z, _Swim); float2 px = uv; fixed4 t = tex2D(_BaseMap, px) * _BaseColor; fixed3 c = t.rgb * i.light; UNITY_APPLY_FOG(i.fogCoord, c); return fixed4(c, 1); }
            ENDCG
        }
        UsePass "Legacy Shaders/VertexLit/SHADOWCASTER"
    }
    FallBack Off
}
