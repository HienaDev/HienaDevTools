using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Animated GIFs: writing (GIF89a, loops forever, one 256-colour palette for all frames that keeps small bright colours,
    /// a delay per frame) and reading (any GIF: local palettes, transparency, interlacing, the frame disposal rules).
    /// Frames are bottom-up Color32 arrays (as GetPixels32 gives them); delays in hundredths of a second.
    /// </summary>
    static class GifCodec
    {
        // ------------------------------------------------------------------ writing

        public static byte[] Write(List<Color32[]> frames, List<int> delaysCs, int w, int h)
        {
            var palette = Palette(frames, 256);
            var lookup = Lookup(palette);
            using (var ms = new MemoryStream())
            using (var o = new BinaryWriter(ms))
            {
                o.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
                o.Write((ushort)w); o.Write((ushort)h);
                o.Write((byte)0xF7); o.Write((byte)0); o.Write((byte)0);
                for (int i = 0; i < 256; i++) { var c = i < palette.Count ? palette[i] : new Color32(0, 0, 0, 255); o.Write(c.r); o.Write(c.g); o.Write(c.b); }
                o.Write((byte)0x21); o.Write((byte)0xFF); o.Write((byte)11); o.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                o.Write((byte)3); o.Write((byte)1); o.Write((ushort)0); o.Write((byte)0);
                var idx = new byte[w * h];
                for (int fi = 0; fi < frames.Count; fi++)
                {
                    var f = frames[fi];
                    int delay = Math.Max(2, Math.Min(65535, delaysCs[fi]));
                    o.Write((byte)0x21); o.Write((byte)0xF9); o.Write((byte)4); o.Write((byte)0x04); o.Write((ushort)delay); o.Write((byte)0); o.Write((byte)0);
                    o.Write((byte)0x2C); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)w); o.Write((ushort)h); o.Write((byte)0);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = f[(h - 1 - y) * w + x];
                            idx[y * w + x] = lookup[(c.r >> 3) << 10 | (c.g >> 3) << 5 | (c.b >> 3)];
                        }
                    Lzw(o, idx, 8);
                }
                o.Write((byte)0x3B);
                o.Flush();
                return ms.ToArray();
            }
        }

        static List<Color32> Palette(List<Color32[]> frames, int max)
        {
            var count = new int[32768];
            // (up to ~30 frames spread over the whole GIF are plenty to find its colours)
            int stride = Math.Max(1, frames.Count / 30);
            for (int fi = 0; fi < frames.Count; fi += stride) { var f = frames[fi]; for (int i = 0; i < f.Length; i += 2) { var c = f[i]; count[(c.r >> 3) << 10 | (c.g >> 3) << 5 | (c.b >> 3)]++; } }
            var keys = new List<int>();
            for (int i = 0; i < count.Length; i++) if (count[i] > 0) keys.Add(i);
            var pal = new List<Color32>();
            if (keys.Count == 0) { pal.Add(new Color32(0, 0, 0, 255)); return pal; }
            Color32 ToC(int k) => new Color32((byte)(((k >> 10) & 31) * 255 / 31), (byte)(((k >> 5) & 31) * 255 / 31), (byte)((k & 31) * 255 / 31), 255);
            if (keys.Count <= max) { foreach (int k in keys) pal.Add(ToC(k)); return pal; }
            var weight = new float[keys.Count]; var near = new float[keys.Count];
            for (int i = 0; i < keys.Count; i++) { weight[i] = (float)Math.Log(1.0 + count[keys[i]]); near[i] = float.MaxValue; }
            int pick = 0;
            for (int i = 1; i < keys.Count; i++) if (count[keys[i]] > count[keys[pick]]) pick = i;
            while (pal.Count < max)
            {
                var pc = ToC(keys[pick]); pal.Add(pc);
                int next = -1; float best = 0f;
                for (int i = 0; i < keys.Count; i++)
                {
                    var c = ToC(keys[i]);
                    int dr = c.r - pc.r, dg = c.g - pc.g, db = c.b - pc.b;
                    float dd = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                    if (dd < near[i]) near[i] = dd;
                    float sc = weight[i] * (float)Math.Sqrt(near[i]);
                    if (sc > best) { best = sc; next = i; }
                }
                if (next < 0) break;
                pick = next;
            }
            return pal;
        }

        static byte[] Lookup(List<Color32> pal)
        {
            var t = new byte[32768];
            for (int k = 0; k < 32768; k++)
            {
                int r = ((k >> 10) & 31) * 255 / 31, g = ((k >> 5) & 31) * 255 / 31, b = (k & 31) * 255 / 31;
                int best = 0, bd = int.MaxValue;
                for (int i = 0; i < pal.Count; i++)
                {
                    int dr = r - pal[i].r, dg = g - pal[i].g, db = b - pal[i].b;
                    int dd = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                    if (dd < bd) { bd = dd; best = i; if (dd == 0) break; }
                }
                t[k] = (byte)best;
            }
            return t;
        }

        static void Lzw(BinaryWriter o, byte[] data, int minCode)
        {
            o.Write((byte)minCode);
            int clear = 1 << minCode, eoi = clear + 1;
            var dict = new Dictionary<int, int>();
            int next = eoi + 1, size = minCode + 1;
            var outBytes = new List<byte>();
            int bitBuf = 0, bitCount = 0;
            void Emit(int code)
            {
                bitBuf |= code << bitCount; bitCount += size;
                while (bitCount >= 8) { outBytes.Add((byte)(bitBuf & 255)); bitBuf >>= 8; bitCount -= 8; }
            }
            Emit(clear);
            int prefix = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                int k = data[i];
                int key = prefix << 8 | k;
                if (dict.TryGetValue(key, out int code)) { prefix = code; continue; }
                Emit(prefix);
                if (next < 4096)
                {
                    dict[key] = next++;
                    if (next > (1 << size) && size < 12) size++;
                }
                else { Emit(clear); dict.Clear(); next = eoi + 1; size = minCode + 1; }
                prefix = k;
            }
            Emit(prefix);
            Emit(eoi);
            if (bitCount > 0) outBytes.Add((byte)(bitBuf & 255));
            for (int i = 0; i < outBytes.Count; i += 255)
            {
                int n = Math.Min(255, outBytes.Count - i);
                o.Write((byte)n);
                for (int j = 0; j < n; j++) o.Write(outBytes[i + j]);
            }
            o.Write((byte)0);
        }

        // ------------------------------------------------------------------ reading

        public class Decoded { public int width, height; public List<Color32[]> frames = new List<Color32[]>(); public List<int> delaysCs = new List<int>(); }

        public static Decoded Read(byte[] d)
        {
            if (d == null || d.Length < 13 || d[0] != 'G' || d[1] != 'I' || d[2] != 'F') throw new Exception("not a GIF file");
            int p = 6;
            int W = d[p] | d[p + 1] << 8, H = d[p + 2] | d[p + 3] << 8; byte packed = d[p + 4]; p += 7;
            Color32[] gct = null;
            if ((packed & 0x80) != 0) { int n = 2 << (packed & 7); gct = ReadTable(d, ref p, n); }
            var res = new Decoded { width = W, height = H };
            var canvas = new Color32[W * H]; // top-down while composing
            Color32[] saved = null;
            int delay = 10, transIndex = -1, disposal = 0;
            int prevDisposal = 0, pl = 0, pt = 0, pw = 0, ph = 0;
            while (p < d.Length)
            {
                byte b = d[p++];
                if (b == 0x3B) break;
                if (b == 0x21)
                {
                    byte label = d[p++];
                    if (label == 0xF9 && d[p] == 4)
                    {
                        byte gp = d[p + 1];
                        disposal = (gp >> 2) & 7;
                        delay = d[p + 2] | d[p + 3] << 8; if (delay < 2) delay = 10;
                        transIndex = (gp & 1) != 0 ? d[p + 4] : -1;
                    }
                    SkipBlocks(d, ref p);
                    continue;
                }
                if (b != 0x2C) break; // (anything else: stop with what we have)
                int l = d[p] | d[p + 1] << 8, t = d[p + 2] | d[p + 3] << 8, w = d[p + 4] | d[p + 5] << 8, h = d[p + 6] | d[p + 7] << 8; byte ip = d[p + 8]; p += 9;
                var table = gct;
                if ((ip & 0x80) != 0) table = ReadTable(d, ref p, 2 << (ip & 7));
                int minCode = d[p++];
                var data = new List<byte>();
                while (p < d.Length) { int n = d[p++]; if (n == 0) break; for (int i = 0; i < n && p < d.Length; i++) data.Add(d[p++]); }
                var idx = LzwDecode(data, minCode, w * h);
                if ((ip & 0x40) != 0) idx = Deinterlace(idx, w, h);

                // the last frame's leftovers
                if (prevDisposal == 2) { for (int y = pt; y < pt + ph && y < H; y++) for (int x = pl; x < pl + pw && x < W; x++) canvas[y * W + x] = new Color32(0, 0, 0, 0); }
                else if (prevDisposal == 3 && saved != null) Array.Copy(saved, canvas, canvas.Length);
                if (disposal == 3) saved = (Color32[])canvas.Clone();

                if (table != null)
                    for (int y = 0; y < h; y++)
                    {
                        int cy = t + y; if (cy >= H) break;
                        for (int x = 0; x < w; x++)
                        {
                            int cx = l + x; if (cx >= W) break;
                            int ci = idx[y * w + x];
                            if (ci == transIndex || ci >= table.Length) continue;
                            canvas[cy * W + cx] = table[ci];
                        }
                    }
                // hand it over bottom-up, transparent parts black
                var frame = new Color32[W * H];
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { var c = canvas[y * W + x]; if (c.a == 0) c = new Color32(0, 0, 0, 255); frame[(H - 1 - y) * W + x] = c; }
                res.frames.Add(frame); res.delaysCs.Add(delay);
                prevDisposal = disposal; pl = l; pt = t; pw = w; ph = h;
                delay = 10; transIndex = -1; disposal = 0;
            }
            if (res.frames.Count == 0) throw new Exception("the GIF has no pictures");
            return res;
        }

        static Color32[] ReadTable(byte[] d, ref int p, int n)
        {
            var t = new Color32[n];
            for (int i = 0; i < n && p + 2 < d.Length; i++, p += 3) t[i] = new Color32(d[p], d[p + 1], d[p + 2], 255);
            return t;
        }

        static void SkipBlocks(byte[] d, ref int p) { while (p < d.Length) { int n = d[p++]; if (n == 0) break; p += n; } }

        static byte[] LzwDecode(List<byte> data, int minCode, int pixels)
        {
            var output = new byte[pixels];
            int clear = 1 << minCode, eoi = clear + 1;
            var prefix = new int[4096]; var suffix = new byte[4096]; var stack = new byte[4097];
            for (int i = 0; i < clear; i++) { prefix[i] = -1; suffix[i] = (byte)i; }
            int codeSize = minCode + 1, mask = (1 << codeSize) - 1, avail = clear + 2, old = -1, first = 0;
            int bits = 0, buf = 0, bi = 0, op = 0;
            while (op < pixels)
            {
                while (bits < codeSize) { if (bi >= data.Count) return output; buf |= data[bi++] << bits; bits += 8; }
                int code = buf & mask; buf >>= codeSize; bits -= codeSize;
                if (code == clear) { codeSize = minCode + 1; mask = (1 << codeSize) - 1; avail = clear + 2; old = -1; continue; }
                if (code == eoi) break;
                if (old == -1) { if (code < clear) { output[op++] = suffix[code]; first = code; } old = code; continue; }
                int inCode = code, sp = 0;
                if (code >= avail) { stack[sp++] = (byte)first; code = old; }
                while (code >= clear && sp < 4096) { stack[sp++] = suffix[code]; code = prefix[code]; if (code < 0) break; }
                if (code < 0) break;
                first = suffix[code];
                stack[sp++] = (byte)first;
                while (sp > 0 && op < pixels) output[op++] = stack[--sp];
                if (avail < 4096)
                {
                    prefix[avail] = old; suffix[avail] = (byte)first; avail++;
                    if ((avail & mask) == 0 && avail < 4096) { codeSize++; mask += avail; }
                }
                old = inCode;
            }
            return output;
        }

        static byte[] Deinterlace(byte[] idx, int w, int h)
        {
            var o = new byte[idx.Length];
            int row = 0;
            foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
                for (int y = start; y < h; y += step, row++)
                    Array.Copy(idx, row * w, o, y * w, w);
            return o;
        }
    }
}
