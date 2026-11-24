using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The Atlas page: pick materials (or objects), see how they share one texture, then make it.</summary>
    public partial class TextureStudioWindow
    {
        [SerializeField] List<Material> m_AtlasMats = new List<Material>();
        [SerializeField] List<Renderer> m_AtlasUsers = new List<Renderer>();
        [SerializeField] TextureAtlas.Options m_AtlasOpt = new TextureAtlas.Options();
        TextureAtlas.Plan m_AtlasPlan; string m_AtlasKey; TextureAtlas.Made m_AtlasMade;

        public void AtlasUse(IEnumerable<Object> objs)
        {
            foreach (var o in objs)
            {
                if (o is Material m) { if (!m_AtlasMats.Contains(m)) m_AtlasMats.Add(m); }
                else if (o is GameObject go)
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                        if (!m_AtlasUsers.Contains(r)) m_AtlasUsers.Add(r);
                        foreach (var mm in r.sharedMaterials) if (mm && !m_AtlasMats.Contains(mm)) m_AtlasMats.Add(mm);
                    }
            }
            m_AtlasPlan = null; m_AtlasMade = null;
        }
        public TextureAtlas.Made AtlasMake() { m_AtlasMade = TextureAtlas.Make(m_AtlasMats, m_AtlasUsers.Where(r => r && !EditorUtility.IsPersistent(r)).ToList(), m_AtlasOpt); m_AtlasPlan = null; return m_AtlasMade; }
        public TextureAtlas.Options AtlasOptions => m_AtlasOpt;

        void AtlasGUI()
        {
            m_AtlasMats.RemoveAll(m => !m); m_AtlasUsers.RemoveAll(r => !r);
            var o = m_AtlasOpt;
            string key = string.Join(",", m_AtlasMats.Select(m => m.GetHashCode())) + "|" + m_AtlasUsers.Count + "|" + o.size + o.shrinkToFit + o.padding + o.maxRepeats;
            if (m_AtlasPlan == null || key != m_AtlasKey) { m_AtlasKey = key; m_AtlasPlan = m_AtlasMats.Count > 0 ? TextureAtlas.Lay(m_AtlasMats, m_AtlasUsers, o) : null; }

            GUILayout.BeginVertical(m_Card);
            GUILayout.Label("Atlas: many materials into one", m_Title);
            GUILayout.Label("Their textures go side by side into one texture (and one normal, shine and glow map), with one material. The objects get new UVs and the one material: fewer draw calls, like the PS1's texture pages. Textures that repeat on a model are baked repeated, so nothing changes on screen.", m_Hint);
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            // ---- left: what goes in, and the settings
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.BeginVertical(m_Card);
            GUILayout.BeginHorizontal();
            var old = GUI.backgroundColor; GUI.backgroundColor = Accent;
            if (GUILayout.Button(new GUIContent("+  Use selection", "Objects in the scene (their materials, and they get the atlas) or materials in the Project"), GUILayout.Height(28))) { var sel = Selection.objects; EditorApplication.delayCall += () => { AtlasUse(sel); Repaint(); }; }
            GUI.backgroundColor = old;
            if (GUILayout.Button("Clear", GUILayout.Height(28), GUILayout.Width(60))) { m_AtlasMats.Clear(); m_AtlasUsers.Clear(); m_AtlasPlan = null; m_AtlasMade = null; }
            GUILayout.EndHorizontal();
            var drop = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(drop, PanelHi); GUI.Label(drop, "or drag materials or objects here", new GUIStyle(m_Hint) { alignment = TextAnchor.MiddleCenter });
            var ev = Event.current;
            if ((ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && drop.Contains(ev.mousePosition)) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); var d = DragAndDrop.objectReferences; EditorApplication.delayCall += () => { AtlasUse(d); Repaint(); }; } ev.Use(); }
            GUILayout.Space(4);
            Material remove = null;
            foreach (var m in m_AtlasMats)
            {
                GUILayout.BeginHorizontal();
                var t = AssetPreview.GetAssetPreview(m) ?? AssetPreview.GetMiniThumbnail(m);
                GUILayout.Label(t, GUILayout.Width(34), GUILayout.Height(34));
                var e = m_AtlasPlan?.entries.FirstOrDefault(x => x.mat == m);
                GUILayout.BeginVertical(); GUILayout.Label(m.name, m_TileTitle);
                GUILayout.Label(e == null ? "" : (e.colour ? e.colour.width + "×" + e.colour.height : "plain colour") + (e.tiles != Vector2Int.one ? "   repeats " + e.tiles.x + "×" + e.tiles.y : "") + "   → " + e.inner.width + "×" + e.inner.height + " px", m_TileSub);
                GUILayout.EndVertical();
                if (GUILayout.Button("×", GUILayout.Width(22))) remove = m;
                GUILayout.EndHorizontal();
            }
            if (remove) { m_AtlasMats.Remove(remove); m_AtlasPlan = null; }
            if (m_AtlasMats.Count == 0) GUILayout.Label("Pick the objects (or materials) and press Use selection.", m_Hint);
            else GUILayout.Label(m_AtlasUsers.Count + " object" + (m_AtlasUsers.Count == 1 ? "" : "s") + " will get the atlas.", m_Hint);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(m_Card);
            GUILayout.Label("SIZE", m_Head);
            GUILayout.BeginHorizontal(); foreach (var s in new[] { 256, 512, 1024, 2048, 4096 }) if (Pill(s.ToString(), o.size == s, s == 256 ? "One PS1 texture page" : "")) o.size = s; GUILayout.EndHorizontal();
            o.shrinkToFit = EditorGUILayout.ToggleLeft(new GUIContent("Smaller if everything fits", "Uses a smaller atlas when the textures fit without shrinking"), o.shrinkToFit);
            GUILayout.BeginHorizontal(); GUILayout.Label("Gap between parts", m_Hint, GUILayout.Width(110)); o.padding = EditorGUILayout.IntSlider(o.padding, 0, 16); GUILayout.EndHorizontal();
            GUILayout.Label("A gap stops colours from the next part bleeding in when far away (mipmaps). Pixel art can use 0.", m_Hint);
            GUILayout.Space(4);
            GUILayout.Label("NAME AND PLACE", m_Head);
            o.name = EditorGUILayout.TextField(o.name); o.folder = EditorGUILayout.TextField(o.folder);
            o.shaderFrom = (Material)EditorGUILayout.ObjectField(new GUIContent("Shader from", "The new material copies this one's shader and settings (empty: the first material)"), o.shaderFrom, typeof(Material), false);
            o.putOnObjects = EditorGUILayout.ToggleLeft(new GUIContent("Put it on the objects", "New meshes with the UVs on the atlas, and the one material (Ctrl+Z puts the old ones back)"), o.putOnObjects);
            GUILayout.Space(6);
            GUI.backgroundColor = Accent;
            using (new EditorGUI.DisabledScope(m_AtlasMats.Count < 1))
                if (GUILayout.Button(new GUIContent("Make the atlas", "Saves the textures and the material in the folder"), new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 }, GUILayout.Height(32))) EditorApplication.delayCall += () => { var r = AtlasMake(); if (r != null) { EditorGUIUtility.PingObject(r.material); ShowNotification(new GUIContent("Made " + r.material.name + (r.objects > 0 ? ", on " + r.objects + " objects" : ""))); } Repaint(); };
            GUI.backgroundColor = old;
            GUILayout.EndVertical();
            GUILayout.EndVertical();

            // ---- right: the layout
            GUILayout.BeginVertical(m_Card);
            var p = m_AtlasPlan;
            GUILayout.Label(m_AtlasMade != null ? "MADE" : p != null ? "THE ATLAS, " + p.size + " × " + p.size + (p.normal ? " · normals" : "") + (p.mask ? " · shine" : "") + (p.glow ? " · glow" : "") + (p.hard ? " · pixel art" : "") : "THE ATLAS", m_Head);
            float side = Mathf.Max(200, Mathf.Min(position.width - 430, position.height - 230));
            var box = GUILayoutUtility.GetRect(side, side, GUILayout.Width(side), GUILayout.Height(side));
            EditorGUI.DrawRect(box, Bg);
            if (m_AtlasMade != null && m_AtlasMade.colour) GUI.DrawTexture(box, m_AtlasMade.colour, ScaleMode.StretchToFill, true);
            else if (p != null)
            {
                float k = box.width / p.size;
                foreach (var e in p.entries)
                {
                    // Unity textures start at the bottom: flip for the screen
                    var r = new Rect(box.x + e.inner.x * k, box.yMax - (e.inner.y + e.inner.height) * k, e.inner.width * k, e.inner.height * k);
                    if (e.colour) GUI.DrawTextureWithTexCoords(r, e.colour, new Rect(0, 0, e.tiles.x, e.tiles.y), true);
                    else { var c = e.mat.HasProperty("_BaseColor") ? e.mat.GetColor("_BaseColor") : e.mat.HasProperty("_Color") ? e.mat.GetColor("_Color") : Color.grey; c.a = 1; EditorGUI.DrawRect(r, c); }
                    if (r.width > 50) GUI.Label(new Rect(r.x + 3, r.y + 2, r.width - 6, 16), e.mat.name, new GUIStyle(EditorStyles.whiteMiniLabel) { clipping = TextClipping.Clip });
                }
            }
            if (p != null && p.warning != "") GUILayout.Label(p.warning, m_Hint);
            if (m_AtlasMade != null) GUILayout.Label(m_AtlasMade.material.name + ": " + (m_AtlasMade.objects > 0 ? m_AtlasMade.objects + " objects use it now (" + m_AtlasMade.meshes + " new meshes)." : "the objects were not changed."), m_Hint);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
    }
}
