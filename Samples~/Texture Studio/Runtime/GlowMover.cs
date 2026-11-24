using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Makes a glowing material alive in the game: breathing, pulsing, flickering, a broken neon sign. Put it on an
    /// object whose material has a glow (Texture Studio's Glow makes one). Other objects with that material don't change.</summary>
    [AddComponentMenu("Dev Tools/Glow Mover")]
    [RequireComponent(typeof(Renderer))]
    public class GlowMover : MonoBehaviour
    {
        public enum How { Breathe, Pulse, Flicker, BrokenNeon, Candle, Blink }
        public How how = How.Breathe;
        [Tooltip("How fast")] [Min(0.01f)] public float speed = 1f;
        [Tooltip("The weakest and the strongest it gets, compared to the material")] public float weakest = 0.25f, strongest = 1.2f;
        [Tooltip("Every object a little different, so they don't all move together")] public bool random = true;
        [Tooltip("A light that follows it (optional)")] public Light follow;
        static readonly int s_Emission = Shader.PropertyToID("_EmissionColor");
        Renderer m_Renderer; MaterialPropertyBlock m_Block; Color m_Full; float m_Off, m_Light;

        void OnEnable()
        {
            m_Renderer = GetComponent<Renderer>(); m_Block = new MaterialPropertyBlock(); var m = m_Renderer.sharedMaterial; m_Full = m && m.HasProperty(s_Emission) ? m.GetColor(s_Emission) : Color.white; m_Off = random ? Random.value * 100f : 0f; if (follow) m_Light = follow.intensity;
        }
        void OnDisable() { if (m_Renderer) { m_Block.Clear(); m_Renderer.SetPropertyBlock(m_Block); } if (follow) follow.intensity = m_Light; }

        void Update()
        {
            float k = Mathf.LerpUnclamped(weakest, strongest, Level((Time.time + m_Off) * speed)); m_Renderer.GetPropertyBlock(m_Block); m_Block.SetColor(s_Emission, m_Full * Mathf.Max(0f, k)); m_Renderer.SetPropertyBlock(m_Block); if (follow) follow.intensity = m_Light * Mathf.Max(0f, k);
        }

        float Level(float t)
        {
            switch (how)
            {
                case How.Pulse: { float b = Mathf.Repeat(t, 1f); return Mathf.Exp(-b * 5f) + 0.6f * Mathf.Exp(-Mathf.Abs(b - 0.22f) * 18f); }
                case How.Flicker: return Mathf.PerlinNoise(t * 9f, 0.3f);
                case How.BrokenNeon: { float n = Mathf.PerlinNoise(t * 1.3f, 7f); return n > 0.58f ? Mathf.PerlinNoise(t * 37f, 1f) > 0.5f ? 1f : 0.05f : 1f; }
                case How.Candle: return 0.55f + 0.3f * Mathf.PerlinNoise(t * 3f, 2f) + 0.2f * Mathf.PerlinNoise(t * 11f, 5f);
                case How.Blink: return Mathf.Repeat(t, 1f) < 0.5f ? 1f : 0f;
                default: return 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
            }
        }
    }
}
