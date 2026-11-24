// Camera feed look (CCTV / security camera) used by CinematicController for multiview cells, pop-ups,
// overlays (images, waveform, progress bar), scan boxes and text. Works in Built-in, URP and HDRP.
// Sizes are in "pixels at 1080p" so the look is the same at any resolution.
Shader "Cinematic/Camera Feed"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Look ("Camera look (0/1)", Float) = 1
        _Tint ("Tint", Color) = (0.85, 1, 0.95, 1)
        _Desaturate ("Desaturate", Range(0, 1)) = 0.7
        _Scanlines ("Scanlines", Range(0, 1)) = 0.5
        _ScanCount ("Scanlines from top to bottom", Float) = 360
        _Noise ("Noise", Range(0, 1)) = 0.35
        _Chromatic ("Chromatic aberration", Range(0, 1)) = 0.35
        _Distortion ("Lens distortion", Range(0, 1)) = 0.3
        _Vignette ("Vignette", Range(0, 1)) = 0.55
        _Glitch ("Glitch", Range(0, 1)) = 0.08
        _Heat ("Thermal look", Range(0, 1)) = 0
        _Blur ("Softness", Range(0, 1)) = 0
        _RecDot ("REC dot (0/1)", Float) = 1
        _Border ("Border (px at 1080p)", Float) = 0
        _BorderColor ("Border color", Color) = (0.45, 1, 0.85, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _TexAlpha ("Use texture alpha (0/1)", Float) = 0
        _Bracket ("Corner brackets (0 = full frame)", Range(0, 0.5)) = 0
        _Mode ("Mode (0 picture, 1 waveform, 2 progress bar, 3 text)", Float) = 0
        _Progress ("Progress", Range(0, 1)) = 0
        _Thickness ("Line thickness (px at 1080p)", Float) = 3
        _UseTex ("Progress bar: fill with the texture (0/1)", Float) = 0
        _Premul ("Texture colours are premultiplied (0/1)", Float) = 0
        _FeedTime ("Time", Float) = 0
        _Seed ("Seed", Float) = 0
        _Size ("Size (px at 1080p)", Vector) = (1920, 1080, 0, 0)
        _Wave ("Waveform: moving from, to, speed, detail", Vector) = (0.35, 0.95, 1, 1)
        _Wave2 ("Waveform: style (0 line, 1 bars, 2 filled), bars", Vector) = (0, 32, 0, 0)
        _Bar ("Progress bar: direction, blocks, frame (0/1), gap px", Vector) = (0, 0, 1, 3)
        _Bar2 ("Progress bar: empty part opacity", Vector) = (0, 0, 0, 0)
        _FillColor ("Progress bar fill colour", Color) = (1, 1, 1, 1)
        _Glow ("Glow: amount, size px, own colours (0/1)", Vector) = (0, 12, 1, 0)
        _BlurStep ("Glow blur step (uv)", Vector) = (0, 0, 0, 0)
        _GlowColor ("Glow colour", Color) = (0.45, 1, 0.85, 1)
        _GlowPad ("Room left around the shape for the glow (0-1)", Vector) = (0, 0, 0, 0)
        _Wipe ("Wiped away from the right (0-1)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Look, _Desaturate, _Scanlines, _Noise, _Chromatic, _Distortion, _Vignette, _Glitch, _Heat, _Blur;
            float _RecDot, _Border, _Alpha, _FeedTime, _Seed, _TexAlpha, _Bracket, _Mode, _Progress, _Thickness, _UseTex, _Premul;
            float4 _Tint, _BorderColor, _Size, _Color;
            float4 _Wave, _Wave2, _Bar, _Bar2, _FillColor, _Glow, _GlowColor, _GlowPad;
            float _Wipe, _ScanCount;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 screen : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float hash (float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Progress bar: frame plus a square fill (smooth or in blocks) in any direction, optionally a sprite
            float4 ProgressBar (float2 uv)
            {
                float2 px = uv * _Size.xy;
                float2 e2 = min(px, _Size.xy - px);
                float frameOn = _Bar.z > 0.5 ? 1.0 : 0.0;
                float frameLine = frameOn * step(min(e2.x, e2.y), _Thickness);
                float pad = frameOn * _Thickness * 2.0;
                float2 inner = max(_Size.xy - 2.0 * pad, 1.0);
                float2 iuv = (px - pad) / inner;
                float inside = step(pad, e2.x) * step(pad, e2.y);

                int dir = (int)(_Bar.x + 0.5);
                float along = iuv.x, len = inner.x;
                if (dir == 1) along = 1.0 - iuv.x;
                else if (dir == 2) { along = iuv.y; len = inner.y; }
                else if (dir == 3) { along = 1.0 - iuv.y; len = inner.y; }
                else if (dir == 4) { along = abs(iuv.x - 0.5) * 2.0; len = inner.x * 0.5; }
                along = saturate(along);

                float filled, block = 1.0;
                float n = floor(_Bar.y + 0.5);
                if (n >= 1.0)
                {
                    // Blocks light up one by one; gaps between them
                    float seg = min(floor(along * n), n - 1.0);
                    filled = step(seg + 1.0, _Progress * n + 0.001);
                    float segLen = len / n;
                    block = step((along * n - seg) * segLen, segLen - _Bar.w);
                }
                else filled = step(along, _Progress) * step(0.0001, _Progress);

                float4 fc = _FillColor;
                if (_UseTex > 0.5) fc *= tex2Dlod(_MainTex, float4(saturate(iuv), 0, 0));
                float on = inside * block * max(filled, _Bar2.x);
                float4 fill = float4(fc.rgb, fc.a * on);
                return frameLine > 0.5 ? float4(1, 1, 1, 1) : fill;
            }

            // Waveform: where it moves across the width (soft at both ends)
            float WaveEnv (float x)
            {
                float from = _Wave.x, to = max(_Wave.y, _Wave.x + 0.001);
                float soft = max((to - from) * 0.25, 0.002);
                return smoothstep(from, from + soft, x) * (1.0 - smoothstep(to - soft, to, x));
            }

            // Waveform height at x (about -1..1): a real sound (texture) or a made-up audio-like wiggle
            float WaveAt (float x)
            {
                if (_UseTex > 0.5)
                {
                    float u = (x - _Wave.x) / max(_Wave.y - _Wave.x, 0.001);
                    return tex2Dlod(_MainTex, float4(saturate(u), 0.5, 0, 0)).r * 2.0 - 1.0;
                }
                float t = _FeedTime * _Wave.z, d = max(_Wave.w, 0.05);
                float level = 0.55 + 0.45 * sin(t * 2.3 + _Seed) * sin(t * 3.7);
                return (sin(x * 60.0 * d + t * 14.0) * 0.5 + sin(x * 137.0 * d - t * 23.0) * 0.3 + sin(x * 29.0 * d + t * 7.0) * 0.2) * level;
            }

            // Coverage (0-1) of the waveform at this spot
            float Shape (float2 uv)
            {
                float2 px = uv * _Size.xy;
                float k = max(abs(ddy(px.y)), 1e-4);  // 1080p pixels per screen pixel
                float amp = _Progress * 0.42;          // half the height, times the waveform's height setting
                // At least one full screen pixel of solid line, so the colour is exactly the one picked
                float halfMin = max(_Thickness * 0.5 / k, 0.75);
                int style = (int)(_Wave2.x + 0.5);
                if (style == 1)
                {
                    // Bars, like an equaliser
                    float n = max(floor(_Wave2.y + 0.5), 1.0);
                    float b = floor(uv.x * n);
                    float xc = (b + 0.5) / n;
                    float h = abs(WaveAt(xc)) * WaveEnv(xc) * amp * _Size.y / k;
                    float fx = abs(frac(uv.x * n) - 0.5) * _Size.x / n / max(abs(ddx(px.x)), 1e-4); // screen px from the bar's middle
                    float halfW = max(_Size.x / n * 0.35 / max(abs(ddx(px.x)), 1e-4), 0.75);
                    float dy = abs(px.y - _Size.y * 0.5) / k - max(h, halfMin);
                    return saturate(0.5 - dy) * saturate(halfW + 0.5 - fx);
                }
                if (style == 2)
                {
                    // Filled, mirrored above and below the middle
                    float h = abs(WaveAt(uv.x)) * WaveEnv(uv.x) * amp * _Size.y / k;
                    float dy = abs(px.y - _Size.y * 0.5) / k - max(h, halfMin);
                    return saturate(0.5 - dy);
                }
                // Line
                float f = (uv.y - (0.5 + WaveAt(uv.x) * WaveEnv(uv.x) * amp)) * _Size.y;
                float g = max(length(float2(ddx(f), ddy(f))), 1e-4);  // keeps the line even on steep parts
                return saturate(halfMin + 0.5 - abs(f) / g);
            }

            // What this layer shows at uv before the camera look: colour and alpha
            float4 Source (float2 uv)
            {
                // Outside the shape (room for the glow) or wiped away: nothing
                if ((_GlowPad.x > 0.0 || _GlowPad.y > 0.0) && (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0)) return float4(0, 0, 0, 0);
                if (uv.x > 1.0 - _Wipe) return float4(0, 0, 0, 0);
                float4 c;
                if (_Mode > 1.5)
                    c = ProgressBar(uv);
                else if (_Mode > 0.5)
                    c = float4(1, 1, 1, Shape(uv));
                else
                {
                    float4 tx = tex2D(_MainTex, uv);
                    if (_Premul > 0.5) tx.rgb /= max(tx.a, 1e-4);
                    c = float4(tx.rgb, lerp(1.0, tx.a, _TexAlpha));
                }
                return c * _Color;
            }

            // Softness: a small blur around the spot
            float4 SourceSoft (float2 uv, float k)
            {
                float4 c0 = Source(uv);
                float b = _Blur * k;
                if (b <= 0.001) return c0;
                float2 r = b * 4.0 / max(_Size.xy, 1.0);
                float4 acc = c0 * 2.0;
                acc += Source(uv + float2(r.x, r.y));
                acc += Source(uv + float2(-r.x, r.y));
                acc += Source(uv + float2(r.x, -r.y));
                acc += Source(uv + float2(-r.x, -r.y));
                return acc / 6.0;
            }

            // Colour grading: desaturate, tint, thermal
            float3 Grade (float3 col, float k)
            {
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lum.xxx, _Desaturate * k);
                col *= lerp(float3(1, 1, 1), _Tint.rgb, k);

                float hx = saturate(dot(col, float3(0.299, 0.587, 0.114)) * 1.2);
                float3 heatCol = lerp(float3(0.03, 0.0, 0.25), float3(0.6, 0.0, 0.65), saturate(hx * 3.0));
                heatCol = lerp(heatCol, float3(1.0, 0.25, 0.0), saturate(hx * 3.0 - 1.0));
                heatCol = lerp(heatCol, float3(1.0, 0.95, 0.35), saturate(hx * 3.0 - 2.0));
                return lerp(col, heatCol, _Heat * k);
            }

            // Effects in screen space: scanlines, rolling bar, grain, flicker, vignette
            // px = position in 1080p pixels, c = position from the centre (-0.5..0.5), v = 0 bottom .. 1 top
            float3 ScreenFx (float3 col, float2 px, float2 c, float v, float k)
            {
                float t = _FeedTime;
                float frame = floor(t * 12.0);
                float lines = sin(px.y * 6.28318 * max(_ScanCount, 1.0) / 1080.0) * 0.5 + 0.5; // px is in 1080p pixels
                col *= 1.0 - _Scanlines * k * 0.35 * lines;
                float roll = frac(v * 0.6 + t * 0.12 + _Seed * 0.1);
                col *= 1.0 + smoothstep(0.85, 1.0, roll) * 0.12 * _Scanlines * k;

                float n = hash(floor(px) + frac(t * 7.3) * 100.0 + _Seed) - 0.5;
                col += n * 0.22 * _Noise * k;
                col *= 1.0 + (hash(float2(frame, _Seed + 3.1)) - 0.5) * 0.08 * _Noise * k;

                col *= saturate(1.0 - dot(c, c) * 2.4 * _Vignette * k);
                return col;
            }

            float4 Content (v2f i, float2 uv, float2 px, float2 c, float k, float t)
            {

                // Frame (full, or corner brackets only), kept crisp
                float2 edge = min(px, _Size.xy - px);
                bool onFrame = _Border > 0 && min(edge.x, edge.y) < _Border;
                if (_Bracket > 0) onFrame = onFrame && max(edge.x, edge.y) < _Bracket * min(_Size.x, _Size.y);
                if (onFrame && (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0 || uv.x > 1.0 - _Wipe)) onFrame = false;
                if (onFrame)
                {
                    float3 fc = ScreenFx(_BorderColor.rgb, px, c, uv.y, k);
                    return float4(saturate(fc), _BorderColor.a * _Alpha * i.color.a);
                }

                // Lens distortion (barrel), scaled so the corners stay in place.
                // UI shapes (waveform, progress bar) keep their shape: no distortion, glitch or colour split.
                float shapeK = _Mode > 0.5 ? 0.0 : k;
                float d = _Distortion * shapeK;
                float2 duv = 0.5 + c * (1.0 + dot(c, c) * d * 0.8) / (1.0 + 0.4 * d);

                // Glitch: some frames tear horizontal bands sideways
                float g = _Glitch * shapeK;
                float frame = floor(t * 12.0);
                float burst = step(1.0 - g * 0.6, hash(float2(frame, _Seed)));
                float row = hash(float2(floor(duv.y * 24.0), frame + _Seed));
                duv.x += burst * step(0.6, row) * (row - 0.8) * 0.25;
                duv.x += (hash(float2(floor(px.y * 0.5), frame)) - 0.5) * 0.0015 * _Noise * shapeK; // tiny line wobble

                // Chromatic aberration, stronger towards the edges and during glitches
                float2 ca = c * (0.012 * _Chromatic + burst * 0.01) * shapeK;
                float4 sg = SourceSoft(duv, k);
                float4 sr = shapeK > 0.0 ? SourceSoft(duv + ca, k) : sg;
                float4 sb = shapeK > 0.0 ? SourceSoft(duv - ca, k) : sg;
                float a = max(sg.a, max(sr.a, sb.a));
                float3 col = float3(sr.r * sr.a, sg.g * sg.a, sb.b * sb.a) / max(a, 1e-4);
                col *= i.color.rgb;
                a *= _Alpha * i.color.a;

                col = Grade(col, k);
                col = ScreenFx(col, px, c, uv.y, k);

                // Blinking REC dot, top right
                float r = _Size.y * 0.03;
                float2 dotCenter = float2(_Size.x - r * 2.2, _Size.y - r * 2.2);
                float on = step(0.5, frac(t * 0.8)) * _RecDot * k;
                col = lerp(col, float3(1.0, 0.15, 0.1), on * saturate(r - length(px - dotCenter)));

                return float4(saturate(col), a);
            }
            float4 frag (v2f i) : SV_Target
            {
                float t = _FeedTime;
                float k = _Look;

                // Text: the font atlas gives the letter shapes; effects use the position on screen
                if (_Mode > 2.5)
                {
                    float2 sp = i.screen.xy / max(i.screen.w, 1e-5);
                    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    float2 spx = sp * float2(1080.0 * aspect, 1080.0);
                    float ta = tex2D(_MainTex, i.uv).a * i.color.a * _Alpha;
                    float3 tcol = Grade(i.color.rgb, k);
                    tcol = ScreenFx(tcol, spx, sp - 0.5, sp.y, k);
                    return float4(saturate(tcol), ta);
                }

                // With a glow the quad is bigger than the shape: uv 0-1 is the shape itself
                float2 uv = (i.uv - _GlowPad.xy) / max(1.0 - 2.0 * _GlowPad.xy, 1e-4);
                float2 px = uv * _Size.xy; // position in pixels at 1080p
                float2 c = uv - 0.5;
                float4 res = Content(i, uv, px, c, k, t);
                return res;
            }
            ENDCG
        }

        // Pass 1: one direction of a soft Gaussian blur (used to make glows). Colours are premultiplied.
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlur
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _BlurStep;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            float4 fragBlur (v2f i) : SV_Target
            {
                const float w[5] = { 0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162 };
                float4 c = tex2D(_MainTex, i.uv) * w[0];
                [unroll] for (int k = 1; k < 5; k++)
                {
                    c += tex2D(_MainTex, i.uv + _BlurStep.xy * k) * w[k];
                    c += tex2D(_MainTex, i.uv - _BlurStep.xy * k) * w[k];
                }
                return c;
            }
            ENDCG
        }

        // Pass 2: the blurred shape drawn as a glow (in one colour, or its own colours)
        Pass
        {
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragGlow
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Glow, _GlowColor;
            float _Alpha;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            float4 fragGlow (v2f i) : SV_Target
            {
                float4 b = tex2D(_MainTex, i.uv);
                // A soft curve so the glow stays bright near the shape and fades out smoothly
                float ga = saturate(1.0 - exp(-b.a * _Glow.x * 3.0)) * _GlowColor.a * _Alpha;
                float3 col = _Glow.z > 0.5 ? b.rgb / max(b.a, 1e-4) : _GlowColor.rgb;
                return float4(col * ga, ga);
            }
            ENDCG
        }
    }
}
