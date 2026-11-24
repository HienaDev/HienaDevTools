using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// Runs a session's steps on a picture and remembers every step's result, so changing one step only reruns it and
    /// the ones after it. Used from a background task (one at a time), so the window never waits.
    /// </summary>
    public class TextureChain
    {
        class Slot { public string key; public Pixels input, output; }
        readonly List<Slot> m_Slots = new List<Slot>();
        public int Reran { get; private set; }
        /// <summary>What went into each step the last time, and what came out at the end.</summary>
        public List<Pixels> Between { get; private set; } = new List<Pixels>();

        public Pixels Run(List<TextureStep> steps, List<string> keys, Pixels start, Vector2Int real, CancellationToken stop, Bands bands = null)
        {
            var p = start; Reran = 0; var between = new List<Pixels> { start };
            for (int i = 0; i < steps.Count; i++)
            {
                stop.ThrowIfCancellationRequested();
                while (m_Slots.Count <= i) m_Slots.Add(new Slot());
                var s = m_Slots[i];
                // (the real size going in is part of what decides the result)
                string key = keys[i] + "|" + real.x + "x" + real.y + "|" + (bands != null ? bands.Key : "");
                if (s.input != p || s.key != key)
                {
                    var run = new TextureRun { final = false, scale = p.w / (float)Mathf.Max(1, real.x) };
                    s.output = TextureBands.Run(steps[i], p, run, bands); s.input = p; s.key = key; Reran++;
                }
                p = s.output; between.Add(p); if (!TextureBands.Around(steps[i], bands)) real = steps[i].SizeAfter(real);
            }
            if (m_Slots.Count > steps.Count) m_Slots.RemoveRange(steps.Count, m_Slots.Count - steps.Count);
            Between = between;
            return p;
        }

        /// <summary>Copies of the switched-on steps that a background task can use while the window keeps changing the originals.</summary>
        public static List<TextureStep> Copy(List<TextureStep> steps, out List<string> keys)
        {
            var list = new List<TextureStep>(); keys = new List<string>();
            foreach (var s in steps)
            {
                if (s == null || !s.on) continue;
                var json = JsonUtility.ToJson(s);
                var copy = (TextureStep)JsonUtility.FromJson(json, s.GetType()); copy.Prepare();
                list.Add(copy); keys.Add(s.GetType().Name + json);
            }
            return list;
        }
    }
}
