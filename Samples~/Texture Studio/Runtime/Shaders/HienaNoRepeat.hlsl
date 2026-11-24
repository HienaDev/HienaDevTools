// Tiling without a visible repeat, for any shader (URP, Built-in, your own):
//
//   #include "HienaNoRepeat.hlsl"   (it is in the Texture Studio sample: Runtime/Shaders, once imported)
//   HienaNR nr = HienaNoRepeat(uv, 1.0, 1.0, float2(0, 0));
//   color = HIENA_NR_SAMPLE(_BaseMap, sampler_BaseMap, nr);      // instead of SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv)
//
// The surface is split in six-sided patches and every patch shows the texture from another random place (and turned
// another way), blended where they meet. It costs three samples per texture instead of one.
#ifndef HIENA_NO_REPEAT_INCLUDED
#define HIENA_NO_REPEAT_INCLUDED

struct HienaNR
{
    float2 uv0, uv1, uv2;   // where each of the three patches samples
    float2 dx, dy;          // how fast the uv changes on screen (before turning)
    float4 rot0, rot1, rot2; // how each patch is turned (2x2)
    float3 w;               // how much each counts
    float3 pick;            // a number 0..1 for each, to choose between several tiles
};

float2 HienaHash2(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.xx + q.yz) * q.zy);
}

float HienaHash1(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// soft blotches, 0..1
float HienaBlotch(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(HienaHash1(i), HienaHash1(i + float2(1, 0)), f.x), lerp(HienaHash1(i + float2(0, 1)), HienaHash1(i + float2(1, 1)), f.x), f.y);
}
float HienaBlotches(float2 p) { return HienaBlotch(p) * 0.6 + HienaBlotch(p * 2.17 + 11.3) * 0.28 + HienaBlotch(p * 4.61 + 5.7) * 0.12; }

void HienaPatch(float2 cell, float2 uv, float size, float turn, float2 snap, out float2 at, out float4 rot, out float pick)
{
    // the middle of this patch
    float2 mid = float2(cell.x + 0.5 * cell.y, cell.y * 0.8660254) / size;
    float2 h = HienaHash2(cell + 17.0); pick = HienaHash1(cell + 41.0);
    // (bricks and tiles: only whole steps, so the rows stay lined up)
    if (snap.x > 0.5) h.x = floor(h.x * snap.x) / snap.x;
    if (snap.y > 0.5) h.y = floor(h.y * snap.y) / snap.y;
    float a = (HienaHash1(cell + 3.0) - 0.5) * 6.2831853 * turn, c = cos(a), s = sin(a);
    rot = float4(c, -s, s, c);
    float2 d = uv - mid;
    at = turn > 0.001 ? float2(dot(rot.xy, d), dot(rot.zw, d)) + mid + h : uv + h;
}

// size: patches per tile (1 is about one patch per tile, smaller numbers make bigger patches). turn: 0..1.
// snap: for textures with a pattern, how many bricks across and down one tile (0 = none).
HienaNR HienaNoRepeat(float2 uv, float size, float turn, float2 snap)
{
    HienaNR o;
    float2 p = uv * size;
    float2 skew = float2(p.x - 0.57735027 * p.y, 1.15470054 * p.y);
    float2 base = floor(skew);
    float3 t; t.xy = frac(skew); t.z = 1.0 - t.x - t.y;
    float s = step(0.0, -t.z), s2 = 2.0 * s - 1.0;
    o.w = float3(-t.z * s2, s - t.y * s2, s - t.x * s2);
    HienaPatch(base + float2(s, s), uv, size, turn, snap, o.uv0, o.rot0, o.pick.x);
    HienaPatch(base + float2(s, 1.0 - s), uv, size, turn, snap, o.uv1, o.rot1, o.pick.y);
    HienaPatch(base + float2(1.0 - s, s), uv, size, turn, snap, o.uv2, o.rot2, o.pick.z);
    o.dx = ddx(uv); o.dy = ddy(uv);
    // mostly one patch, with a narrow blend where they meet
    o.w = pow(saturate(o.w), 5.0);
    o.w /= max(1e-4, o.w.x + o.w.y + o.w.z);
    return o;
}

// which of several tiles a patch shows: the first one gets its own share (rare), the others split the rest
float HienaTile(float pick, float count, float rare) { return pick < rare || count < 1.5 ? 0.0 : 1.0 + min(count - 2.0, floor((pick - rare) / max(1e-4, 1.0 - rare) * (count - 1.0))); }

float2 HienaTurn(float4 r, float2 v) { return float2(dot(r.xy, v), dot(r.zw, v)); }
float2 HienaTurnBack(float4 r, float2 v) { return float2(dot(r.xz, v), dot(r.yw, v)); }

// the bright parts win where patches meet, so shapes stay whole instead of fading into each other
float3 HienaWeights(float3 w, float3 bright)
{
    float3 k = w * (0.05 + bright * bright);
    return k / max(1e-4, k.x + k.y + k.z);
}

#define HIENA_NR_GRAD(tex, samp, nr, n) SAMPLE_TEXTURE2D_GRAD(tex, samp, nr.uv##n, HienaTurn(nr.rot##n, nr.dx), HienaTurn(nr.rot##n, nr.dy))

// samples a texture; nr.w is changed to the weights that were used, so the maps that go with it match when they come after
#define HIENA_NR_COLOUR(outColour, tex, samp, nr) { \
    float4 hc0 = HIENA_NR_GRAD(tex, samp, nr, 0), hc1 = HIENA_NR_GRAD(tex, samp, nr, 1), hc2 = HIENA_NR_GRAD(tex, samp, nr, 2); \
    float3 hl = float3(dot(hc0.rgb, float3(0.299, 0.587, 0.114)), dot(hc1.rgb, float3(0.299, 0.587, 0.114)), dot(hc2.rgb, float3(0.299, 0.587, 0.114))); \
    nr.w = HienaWeights(nr.w, hl); outColour = hc0 * nr.w.x + hc1 * nr.w.y + hc2 * nr.w.z; }

#define HIENA_NR_SAMPLE(tex, samp, nr) (HIENA_NR_GRAD(tex, samp, nr, 0) * nr.w.x + HIENA_NR_GRAD(tex, samp, nr, 1) * nr.w.y + HIENA_NR_GRAD(tex, samp, nr, 2) * nr.w.z)

#endif
