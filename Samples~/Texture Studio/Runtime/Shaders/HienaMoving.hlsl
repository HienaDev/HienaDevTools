// The effects of HienaDev/Moving (Texture Studio's shader maker), shared by its URP and Built-in passes.
#ifndef HIENA_MOVING_INCLUDED
#define HIENA_MOVING_INCLUDED
    // ---- shared by both pipelines
sampler2D _BaseMap, _DetailMap, _FlowMap, _EmissionMap, _UpMap, _HighMap, _LowMap;
float4 _UpMap_ST, _HighMap_ST, _LowMap_ST, _UpColor, _HighColor, _LowColor;
float _UseUp, _UseHigh, _UseLow, _UpAmount, _HighY, _LowY, _LayerBlend, _LayerNoise, _LayerNoiseSize, _UpOnlyAbove, _UpFromY;
float4 _Flag; float _FlagShade;
float4 _BaseMap_ST, _DetailMap_ST, _BaseColor, _Scroll, _Grid, _EmissionColor, _DissolveEdgeColor, _HoloColor, _Wind, _Waves, _RimColor, _Wobble;
float _Cutoff, _DetailMix, _FPS, _FlipBlend, _FlowSpeed, _FlowStrength, _PulseSpeed, _PulseMin, _Dissolve, _DissolveScale, _DissolveEdge, _DissolveLoop, _HoloLines, _HoloSpeed, _HoloFlicker, _RimPower, _ToonSteps, _HueSpeed, _Surface;

float _HmTime, _HmUseTime;
// the time (the editor's preview can give its own, so it moves while nothing plays)
#define HmT() (_HmUseTime > 0.5 ? _HmTime : _Time.y)
float HmHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float HmNoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(HmHash(i), HmHash(i + float2(1, 0)), f.x), lerp(HmHash(i + float2(0, 1)), HmHash(i + float2(1, 1)), f.x), f.y); }
float HmFbm(float2 p) { return HmNoise(p) * 0.55 + HmNoise(p * 2.1) * 0.3 + HmNoise(p * 4.3) * 0.15; }
float3 HmHue(float3 c, float a) { const float3 k = float3(0.57735, 0.57735, 0.57735); float ca = cos(a); return c * ca + cross(k, c) * sin(a) + k * dot(k, c) * (1 - ca); }

// moves a vertex (object space) for wind and waves; t is time
// a flag: waves travelling away from the pole (u = 0), bigger towards the free end, plus a smaller cross ripple.
// x: how far the cloth moves along its normal, y: how steep it is along u (for the folds' light and dark)
float2 HmFlagWave(float2 uv, float t)
{
    float k = 6.2832 * _Flag.z, ph = uv.x * k - t * _Flag.y, ph2 = uv.x * k * 1.9 + uv.y * 3.7 - t * _Flag.y * 1.55;
    float amp = _Flag.x * (0.15 + uv.x) / 1.15, w = sin(ph) + 0.35 * sin(ph2);
    return float2(w * amp, (cos(ph) * k + 0.35 * cos(ph2) * k * 1.9) * amp + w * _Flag.x / 1.15);
}

float3 HmBend(float3 p, float t)
{
    #if defined(_WIND)
        float h = saturate(p.y / max(0.01, _Wind.w)); float s = sin(t * _Wind.y + (p.x + p.z) * _Wind.z) + sin(t * _Wind.y * 1.7 + p.x * _Wind.z * 0.6) * 0.4;
        p.x += s * _Wind.x * h * h; p.z += s * _Wind.x * 0.4 * h * h;
    #endif
    #if defined(_WAVES)
        float k = 6.2832 / max(0.01, _Waves.y); p.y += (sin(p.x * k + t * _Waves.z) + sin(p.z * k * 0.8 + t * _Waves.z * 1.3)) * _Waves.x * 0.5;
    #endif
    return p;
}

// the colour (and alpha) at a spot: every texture effect
float4 HmColour(float2 uv0, float t, float3 worldPos, float3 worldNormal, float fresnel, out float3 glow)
{
    float2 uv = uv0 * _BaseMap_ST.xy + _BaseMap_ST.zw; glow = 0;
    #if defined(_WOBBLE)
        uv += (float2(HmNoise(uv * _Wobble.z + t * _Wobble.y), HmNoise(uv * _Wobble.z - t * _Wobble.y + 17)) - 0.5) * _Wobble.x * 2;
    #endif
    #if defined(_SCROLL)
        uv += _Scroll.xy * t;
    #endif
    float4 c;
    #if defined(_FLIPBOOK)
        float frames = max(1, _Grid.x * _Grid.y), f = t * _FPS, f0 = fmod(floor(f), frames), f1 = fmod(f0 + 1, frames);
        float2 cell = 1 / max(float2(1, 1), _Grid.xy), fu = frac(uv);
        float2 a = (fu + float2(fmod(f0, _Grid.x), _Grid.y - 1 - floor(f0 / _Grid.x))) * cell, b = (fu + float2(fmod(f1, _Grid.x), _Grid.y - 1 - floor(f1 / _Grid.x))) * cell;
        c = lerp(tex2D(_BaseMap, a), tex2D(_BaseMap, b), frac(f) * _FlipBlend);
    #elif defined(_FLOW)
        float2 fl = (tex2D(_FlowMap, uv0).rg * 2 - 1) * _FlowStrength; float p0 = frac(t * _FlowSpeed), p1 = frac(t * _FlowSpeed + 0.5); float w = abs((0.5 - p0) / 0.5);
        c = lerp(tex2D(_BaseMap, uv - fl * p0), tex2D(_BaseMap, uv - fl * p1 + 0.37), w);
    #else
        c = tex2D(_BaseMap, uv);
    #endif
    #if defined(_SCROLL)
        float4 d = tex2D(_DetailMap, uv0 * _DetailMap_ST.xy + _DetailMap_ST.zw + _Scroll.zw * t); c.rgb = lerp(c.rgb, c.rgb * d.rgb * 2, _DetailMix * d.a);
    #endif
    c *= _BaseColor;
    #if defined(_FLAG)
        { float2 fw = HmFlagWave(uv0, t); float amp = max(1e-3, _Flag.x * (0.15 + uv0.x) / 1.15); c.rgb *= max(0, 1 + _FlagShade * (clamp(-fw.y * 0.3, -0.55, 0.55) + clamp(fw.x / amp, -1, 1) * 0.2)); }
    #endif
    #if defined(_LAYERS)
        // other textures by height (lower down, higher up) and on faces that point up (snow), with soft or hard edges
        float rag = (HmFbm(worldPos.xz * _LayerNoiseSize + worldPos.y * 0.7) - 0.5) * _LayerNoise; float bw = max(1e-4, _LayerBlend * 0.5); float hy = worldPos.y + rag * 2;
        if (_UseLow > 0.5) c = lerp(c, tex2D(_LowMap, uv0 * _LowMap_ST.xy + _LowMap_ST.zw) * _LowColor, 1 - smoothstep(_LowY - bw, _LowY + bw, hy));
        if (_UseHigh > 0.5) c = lerp(c, tex2D(_HighMap, uv0 * _HighMap_ST.xy + _HighMap_ST.zw) * _HighColor, smoothstep(_HighY - bw, _HighY + bw, hy));
        if (_UseUp > 0.5) { float up = normalize(worldNormal).y + rag; float edge = 1.02 - _UpAmount * 1.2; float sw = max(1e-4, _LayerBlend * 0.25); float k = smoothstep(edge - sw, edge + sw, up); if (_UpOnlyAbove > 0.5) k *= smoothstep(_UpFromY - bw, _UpFromY + bw, hy); c = lerp(c, tex2D(_UpMap, uv0 * _UpMap_ST.xy + _UpMap_ST.zw) * _UpColor, k); }
    #endif
    #if defined(_HUE)
        c.rgb = HmHue(c.rgb, t * _HueSpeed * 6.2832);
    #endif
    #if defined(_PULSE)
        glow += tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb * lerp(_PulseMin, 1, 0.5 + 0.5 * sin(t * _PulseSpeed * 6.2832));
    #endif
    #if defined(_DISSOLVE)
        float amt = _DissolveLoop > 0 ? 0.5 + 0.5 * sin(t * _DissolveLoop) : _Dissolve; float n = HmFbm(uv0 * _DissolveScale);
        clip(n - amt * 1.02 + 0.001); glow += _DissolveEdgeColor.rgb * step(n, amt + _DissolveEdge) * step(0.001, amt);
    #endif
    #if defined(_HOLO)
        float scan = frac(worldPos.y * _HoloLines * 0.1 - t * _HoloSpeed); float flick = 1 - _HoloFlicker * step(0.92, HmHash(float2(floor(t * 20), 3)));
        float holo = (0.35 + 0.65 * step(0.5, scan)) * flick; c.rgb = lerp(c.rgb, _HoloColor.rgb, 0.6); c.a *= saturate(holo * 0.6 + fresnel * 1.2); glow += _HoloColor.rgb * fresnel * 0.8 * flick;
    #endif
    #if defined(_RIM)
        glow += _RimColor.rgb * pow(fresnel, _RimPower);
    #endif
    return c;
}
float HmToon(float l) {
    #if defined(_TOON)
        return floor(l * _ToonSteps + 0.5) / max(1, _ToonSteps);
    #else
        return l;
    #endif
}
#endif
