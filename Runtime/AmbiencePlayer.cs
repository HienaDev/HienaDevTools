using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A background that's never the same twice: a loop, with sounds (birds, creaks, drips...) scattered over it at
    /// random. Audio Trimmer's Ambience page makes one.</summary>
    [AddComponentMenu("Dev Tools/Ambience Player")]
    public class AmbiencePlayer : MonoBehaviour
    {
        [System.Serializable]
        public class Scatter
        {
            public string name = "Sounds"; public bool on = true; public List<AudioClip> clips = new List<AudioClip>();
            [Tooltip("Seconds between two of them")] public Vector2 every = new Vector2(4, 12); [Tooltip("How loud")] public Vector2 volume = new Vector2(0.4f, 0.9f); [Tooltip("Semitones up and down")] public float pitch = 1.5f; [Tooltip("How wide around you, 0 to 1")] public float wide = 0.7f; [Tooltip("How far away, 0 to 1: quieter, duller")] public float far = 0.3f; [Tooltip("Only to tell them apart in the editor")] public Color colour = new Color(0, 0, 0, 0);
        }

        public AudioClip loop; [Range(0, 1)] public float loopVolume = 0.6f; [Range(0, 1)] public float volume = 1f; public List<Scatter> scatter = new List<Scatter>();
        [Tooltip("Fades in when it starts (seconds)")] public float fadeIn = 2f; [Tooltip("Play through this mixer group (optional)")] public UnityEngine.Audio.AudioMixerGroup output;
        AudioSource m_Loop; readonly List<AudioSource> m_Pool = new List<AudioSource>(); float[] m_Next; AudioClip[] m_Last; float m_Start;

        void OnEnable()
        {
            m_Start = Time.time; if (loop) { m_Loop = gameObject.AddComponent<AudioSource>(); m_Loop.clip = loop; m_Loop.loop = true; m_Loop.spatialBlend = 0; m_Loop.volume = 0; m_Loop.outputAudioMixerGroup = output; m_Loop.Play(); m_Loop.time = Random.value * Mathf.Max(0, loop.length - 0.1f); }
            m_Next = new float[scatter.Count]; m_Last = new AudioClip[scatter.Count]; for (int i = 0; i < m_Next.Length; i++) m_Next[i] = Time.time + Random.Range(0.2f, Mathf.Max(0.3f, scatter[i].every.y));
        }
        void OnDisable() { if (m_Loop) Destroy(m_Loop); foreach (var s in m_Pool) if (s) Destroy(s.gameObject); m_Pool.Clear(); }

        void Update()
        {
            float fade = fadeIn > 0 ? Mathf.Clamp01((Time.time - m_Start) / fadeIn) : 1; if (m_Loop) m_Loop.volume = loopVolume * volume * fade;
            for (int i = 0; i < scatter.Count && i < m_Next.Length; i++) { var s = scatter[i]; if (!s.on || s.clips.Count == 0 || Time.time < m_Next[i]) continue; m_Next[i] = Time.time + Random.Range(Mathf.Min(s.every.x, s.every.y), Mathf.Max(s.every.x, s.every.y)); Play(s, i, fade); }
        }

        void Play(Scatter s, int i, float fade)
        {
            // (not the same one twice in a row)
            var c = s.clips[Random.Range(0, s.clips.Count)]; if (s.clips.Count > 1) for (int t = 0; t < 4 && c == m_Last[i]; t++) c = s.clips[Random.Range(0, s.clips.Count)]; if (!c) return; m_Last[i] = c;
            AudioSource a = null; foreach (var p in m_Pool) if (p && !p.isPlaying) { a = p; break; }
            if (!a) { var go = new GameObject("Ambience sound"); go.transform.SetParent(transform, false); a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 0; go.AddComponent<AudioLowPassFilter>(); m_Pool.Add(a); }
            a.outputAudioMixerGroup = output; a.clip = c; a.pitch = Mathf.Pow(2f, Random.Range(-s.pitch, s.pitch) / 12f); a.volume = Random.Range(s.volume.x, s.volume.y) * volume * fade * (1 - s.far * 0.6f); a.panStereo = Random.Range(-s.wide, s.wide); a.GetComponent<AudioLowPassFilter>().cutoffFrequency = Mathf.Lerp(22000f, 1200f, Mathf.Pow(s.far, 0.6f)); a.Play();
        }
    }
}
