using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Auto Fit UI's project switches, kept in a Resources asset so builds can read them
    /// (Assets/Resources/AutoFitUISettings.asset, made by Tools > Auto Fit UI).</summary>
    public class AutoFitSettings : ScriptableObject
    {
        [Tooltip("Every screen-space Canvas in every scene fits itself when the game runs, no setup needed")]
        public bool fitEveryCanvas = true;

        [Tooltip("The game always shows at one shape (16:9 unless changed), whatever the screen: black bars fill the rest, and the UI stays inside")]
        public bool lockAspect;
        public Vector2 aspect = new Vector2(16, 9);
        public Color barColour = Color.black;

        static AutoFitSettings s_Get; static bool s_Looked;
        public static AutoFitSettings Get { get { if (!s_Looked || (s_Get == null && !Application.isPlaying)) { s_Get = Resources.Load<AutoFitSettings>("AutoFitUISettings"); s_Looked = true; } return s_Get; } }
        /// <summary>(the editor calls this after changing the asset)</summary>
        public static void Forget() { s_Looked = false; s_Get = null; }
    }
}
