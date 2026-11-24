using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Plays the calm part and the hits of a song (Audio Trimmer > Stems) together, in step, and fades the hits in
    /// and out with Intensity: 0 calm, 1 the whole song. Set it from your game when a fight starts or ends.</summary>
    [AddComponentMenu("Dev Tools/Music Layers")]
    public class MusicLayers : MonoBehaviour
    {
        public AudioClip calm, hits; [Range(0, 1)] public float volume = 0.8f;
        [Range(0, 1), Tooltip("0 calm, 1 the whole song")] public float intensity; [Tooltip("Seconds to fade the hits in or out")] public float fade = 2f; public bool playOnStart = true; public UnityEngine.Audio.AudioMixerGroup output;
        AudioSource m_Calm, m_Hits; float m_Now;
        public float Intensity { get => intensity; set => intensity = Mathf.Clamp01(value); }

        void Start() { if (playOnStart) Play(); }
        public void Play()
        {
            if (!calm) return; if (!m_Calm) m_Calm = Source(); if (!m_Hits) m_Hits = Source(); m_Calm.clip = calm; m_Hits.clip = hits; m_Now = intensity;
            // (both start on the same audio tick, so they stay in step)
            double at = AudioSettings.dspTime + 0.1; m_Calm.PlayScheduled(at); if (hits) m_Hits.PlayScheduled(at); Apply();
        }
        public void Stop() { if (m_Calm) m_Calm.Stop(); if (m_Hits) m_Hits.Stop(); }
        AudioSource Source() { var s = gameObject.AddComponent<AudioSource>(); s.loop = true; s.playOnAwake = false; s.spatialBlend = 0; s.outputAudioMixerGroup = output; return s; }
        void Update() { if (!m_Calm) return; m_Now = Mathf.MoveTowards(m_Now, intensity, Time.unscaledDeltaTime / Mathf.Max(0.01f, fade)); Apply(); }
        void Apply() { m_Calm.volume = volume; if (m_Hits) m_Hits.volume = volume * Mathf.SmoothStep(0, 1, m_Now); }
    }
}
