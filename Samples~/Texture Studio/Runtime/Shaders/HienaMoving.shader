// HienaDev/Moving: the Texture Studio shader maker's shader. Every effect is a switch (a keyword), so a material only pays
// for what it uses: scrolling, flipbooks, flow maps, pulsing glow, dissolve, hologram, wind, waves, rim light, toon steps,
// colour cycling, wobble and billboards. URP first, with a simpler Built-in pass.
Shader "HienaDev/Moving"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1,1,1,1)
        _Cutoff ("Cut-out", Range(0,1)) = 0.5
        [Toggle(_UNLIT)] _Unlit ("Unlit", Float) = 0

        [Toggle(_SCROLL)] _UseScroll ("Scroll", Float) = 0
        _Scroll ("Scroll speed (xy), second layer (zw)", Vector) = (0, 0.2, 0, 0)
        _DetailMap ("Second layer", 2D) = "black" {}
        _DetailMix ("Second layer amount", Range(0,1)) = 0

        [Toggle(_FLIPBOOK)] _UseFlip ("Flipbook", Float) = 0
        _Grid ("Columns, rows", Vector) = (4, 4, 0, 0)
        _FPS ("Frames a second", Float) = 12
        _FlipBlend ("Blend frames", Range(0,1)) = 1

        [Toggle(_FLOW)] _UseFlow ("Flow map", Float) = 0
        _FlowMap ("Flow map", 2D) = "grey" {}
        _FlowSpeed ("Flow speed", Float) = 0.3
        _FlowStrength ("Flow strength", Float) = 0.3

        [Toggle(_PULSE)] _UsePulse ("Pulse glow", Float) = 0
        [HDR] _EmissionColor ("Glow", Color) = (1, 0.5, 0.1, 1)
        _EmissionMap ("Glow map", 2D) = "white" {}
        _PulseSpeed ("Pulse speed", Float) = 1
        _PulseMin ("Pulse low", Range(0,1)) = 0.3

        [Toggle(_DISSOLVE)] _UseDissolve ("Dissolve", Float) = 0
        _Dissolve ("Dissolved", Range(0,1)) = 0.3
        _DissolveScale ("Dissolve size", Float) = 8
        _DissolveEdge ("Edge width", Range(0,0.3)) = 0.06
        [HDR] _DissolveEdgeColor ("Edge colour", Color) = (3, 1.2, 0.2, 1)
        _DissolveLoop ("Dissolve by itself (speed)", Float) = 0

        [Toggle(_HOLO)] _UseHolo ("Hologram", Float) = 0
        [HDR] _HoloColor ("Hologram colour", Color) = (0.3, 1.5, 2, 1)
        _HoloLines ("Lines", Float) = 60
        _HoloSpeed ("Line speed", Float) = 1
        _HoloFlicker ("Flicker", Range(0,1)) = 0.2

        [Toggle(_WIND)] _UseWind ("Wind sway", Float) = 0
        _Wind ("Strength, speed, size, height", Vector) = (0.15, 1.5, 1, 1)

        [Toggle(_WAVES)] _UseWaves ("Waves", Float) = 0
        _Waves ("Height, length, speed", Vector) = (0.1, 2, 1, 0)

        [Toggle(_RIM)] _UseRim ("Rim light", Float) = 0
        [HDR] _RimColor ("Rim colour", Color) = (0.4, 0.8, 1.5, 1)
        _RimPower ("Rim width", Range(0.5, 8)) = 3

        [Toggle(_TOON)] _UseToon ("Toon steps", Float) = 0
        _ToonSteps ("Steps", Range(1, 8)) = 3

        [Toggle(_HUE)] _UseHue ("Colour cycle", Float) = 0
        _HueSpeed ("Cycle speed", Float) = 0.2

        [Toggle(_WOBBLE)] _UseWobble ("Wobble", Float) = 0
        _Wobble ("Strength, speed, size", Vector) = (0.02, 2, 6, 0)

        [Toggle(_BILLBOARD)] _UseBillboard ("Face the camera", Float) = 0

        [Toggle(_FLAG)] _UseFlag ("Flag", Float) = 0
        _Flag ("Strength, speed, waves, droop", Vector) = (0.14, 3.5, 1.6, 0.12)
        _FlagShade ("Fold shading", Range(0, 1.5)) = 0.7

        [Toggle(_LAYERS)] _UseLayers ("Layers by height and slope", Float) = 0
        [Toggle] _UseUp ("On faces that point up", Float) = 1
        _UpMap ("On top (snow, moss, dust)", 2D) = "white" {}
        _UpColor ("On top colour", Color) = (0.95, 0.97, 1, 1)
        _UpAmount ("How much is covered", Range(0,1)) = 0.4
        [Toggle] _UpOnlyAbove ("Only above a height", Float) = 0
        _UpFromY ("Only above (world height)", Float) = 0
        [Toggle] _UseHigh ("Higher up", Float) = 0
        _HighMap ("Higher up", 2D) = "white" {}
        _HighColor ("Higher up colour", Color) = (1, 1, 1, 1)
        _HighY ("Higher up from (world height)", Float) = 0.25
        [Toggle] _UseLow ("Lower down", Float) = 0
        _LowMap ("Lower down", 2D) = "white" {}
        _LowColor ("Lower down colour", Color) = (1, 1, 1, 1)
        _LowY ("Lower down below (world height)", Float) = -0.25
        _LayerBlend ("Blend (0: hard edge)", Range(0, 2)) = 0.15
        _LayerNoise ("Ragged edges", Range(0, 1)) = 0.3
        _LayerNoiseSize ("Ragged size", Float) = 3

        [HideInInspector] _HmTime ("Preview time", Float) = 0
        [HideInInspector] _HmUseTime ("Use the preview time", Float) = 0
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }


    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit" Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite] Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _UNLIT
            #pragma shader_feature_local _SCROLL
            #pragma shader_feature_local _FLIPBOOK
            #pragma shader_feature_local _FLOW
            #pragma shader_feature_local _PULSE
            #pragma shader_feature_local _DISSOLVE
            #pragma shader_feature_local _HOLO
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _WAVES
            #pragma shader_feature_local _RIM
            #pragma shader_feature_local _TOON
            #pragma shader_feature_local _HUE
            #pragma shader_feature_local _WOBBLE
            #pragma shader_feature_local _BILLBOARD
            #pragma shader_feature_local _LAYERS
            #pragma shader_feature_local _FLAG
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HienaMoving.hlsl"
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; float3 wn : TEXCOORD2; float4 col : COLOR; };
            V vert(A i)
            {
                V o; float t = HmT(); float3 p = HmBend(i.pos.xyz, t); float3 wp; float3 nObj = i.n;
                #if defined(_FLAG)
                    float2 fw = HmFlagWave(i.uv, t); p += i.n * fw.x; p.y -= _Flag.w * i.uv.x * i.uv.x * 0.3; nObj = normalize(i.n - float3(1, 0, 0) * fw.y);
                #endif
                #if defined(_BILLBOARD)
                    float3 c = TransformObjectToWorld(float3(0, 0, 0)); float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz; float3 sc = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), 1); wp = c + right * p.x * sc.x + up * p.y * sc.y; o.wn = -UNITY_MATRIX_V[2].xyz;
                #else
                    wp = TransformObjectToWorld(p); o.wn = TransformObjectToWorldNormal(nObj);
                #endif
                o.pos = TransformWorldToHClip(wp); o.wp = wp; o.uv = i.uv; o.col = i.col; return o;
            }
            float4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.wn); float3 v = normalize(GetWorldSpaceViewDir(i.wp)); float fres = 1 - saturate(dot(n, v)); float3 glow;
                float4 c = HmColour(i.uv, HmT(), i.wp, n, fres, glow) * i.col;
                #if defined(_ALPHATEST_ON)
                    clip(c.a - _Cutoff);
                #endif
                #if !defined(_UNLIT)
                    Light l = GetMainLight(); float d = HmToon(saturate(dot(n, l.direction))); c.rgb *= l.color * d + SampleSH(n);
                #endif
                c.rgb += glow; return c;
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite] Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _UNLIT
            #pragma shader_feature_local _SCROLL
            #pragma shader_feature_local _FLIPBOOK
            #pragma shader_feature_local _FLOW
            #pragma shader_feature_local _PULSE
            #pragma shader_feature_local _DISSOLVE
            #pragma shader_feature_local _HOLO
            #pragma shader_feature_local _WIND
            #pragma shader_feature_local _WAVES
            #pragma shader_feature_local _RIM
            #pragma shader_feature_local _TOON
            #pragma shader_feature_local _HUE
            #pragma shader_feature_local _WOBBLE
            #pragma shader_feature_local _BILLBOARD
            #pragma shader_feature_local _LAYERS
            #pragma shader_feature_local _FLAG
            #pragma shader_feature_local _ALPHATEST_ON
            #include "UnityCG.cginc"
            #include "HienaMoving.hlsl"
            float4 _LightColor0;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; float3 wn : TEXCOORD2; float4 col : COLOR; };
            V vert(A i)
            {
                V o; float t = HmT(); float3 p = HmBend(i.pos.xyz, t); float3 wp; float3 nObj = i.n;
                #if defined(_FLAG)
                    float2 fw = HmFlagWave(i.uv, t); p += i.n * fw.x; p.y -= _Flag.w * i.uv.x * i.uv.x * 0.3; nObj = normalize(i.n - float3(1, 0, 0) * fw.y);
                #endif
                #if defined(_BILLBOARD)
                    float3 c = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz; wp = c + UNITY_MATRIX_V[0].xyz * p.x + UNITY_MATRIX_V[1].xyz * p.y; o.wn = -UNITY_MATRIX_V[2].xyz;
                #else
                    wp = mul(unity_ObjectToWorld, float4(p, 1)).xyz; o.wn = UnityObjectToWorldNormal(nObj);
                #endif
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1)); o.wp = wp; o.uv = i.uv; o.col = i.col; return o;
            }
            float4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.wn); float3 v = normalize(_WorldSpaceCameraPos - i.wp); float fres = 1 - saturate(dot(n, v)); float3 glow;
                float4 c = HmColour(i.uv, HmT(), i.wp, n, fres, glow) * i.col;
                #if defined(_ALPHATEST_ON)
                    clip(c.a - _Cutoff);
                #endif
                #if !defined(_UNLIT)
                    float d = HmToon(saturate(dot(n, normalize(_WorldSpaceLightPos0.xyz)))); c.rgb *= _LightColor0.rgb * d + ShadeSH9(float4(n, 1));
                #endif
                c.rgb += glow; return c;
            }
            ENDCG
        }
    }
}
