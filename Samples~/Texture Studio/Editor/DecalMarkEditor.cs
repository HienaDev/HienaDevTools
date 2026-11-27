using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Pick a placed decal (click it in the Scene view) to move it over the surfaces, turn it, size it or bend it
    /// round corners, with handles on it and sliders in the Inspector.</summary>
    [CustomEditor(typeof(DecalMark)), CanEditMultipleObjects]
    public class DecalMarkEditor : Editor
    {
        DecalMark M => (DecalMark)target;
        bool Placed => M && !string.IsNullOrEmpty(M.group) && M.transform.parent;

        public override void OnInspectorGUI()
        {
            if (!Placed) { EditorGUILayout.HelpBox(M && M.GetComponent<PaintedLayer>() ? "Paint from Texture Studio's Paint page. Paint over it there, or rub it out with Ctrl." : "Made by Texture Studio.", MessageType.None); return; }
            EditorGUILayout.HelpBox("A decal from Texture Studio. Drag the orange dot in the Scene view to slide it over the surfaces, the ring to turn it and the square to size it. Or use these.", MessageType.None);
            var m = M; float size = m.size, turn = m.turn, aspect = m.aspect, depth = m.depth; bool wrap = m.wrap; EditorGUI.BeginChangeCheck();
            size = EditorGUILayout.Slider("Size", size, 0.02f, 20f); turn = EditorGUILayout.Slider("Turn", Mathf.Repeat(turn + 180, 360) - 180, -180, 180); aspect = EditorGUILayout.Slider(new GUIContent("Stretch", "Taller or wider"), aspect, 0.2f, 5f); depth = EditorGUILayout.Slider(new GUIContent("Reach", "How far over bumps and into gaps it goes"), depth, 0.05f, 1.5f); wrap = EditorGUILayout.Toggle(new GUIContent("Bend round corners"), wrap);
            if (EditorGUI.EndChangeCheck()) Remake(m, m.transform.parent.TransformPoint(m.at), m.transform.parent.TransformDirection(m.normal), size, turn, aspect, depth, wrap);
            var sh = DecalPlacer.SheetOf(m.GetComponent<MeshRenderer>() ? m.GetComponent<MeshRenderer>().sharedMaterial : null); if (sh.count > 1) { int c = EditorGUILayout.IntSlider(new GUIContent("Which one", "It comes from a sheet: pick another of its pictures"), Mathf.Max(0, m.cell) + 1, 1, sh.count) - 1; if (c != m.cell) { m.cell = c; Remake(m, m.transform.parent.TransformPoint(m.at), m.transform.parent.TransformDirection(m.normal), m.size, m.turn, m.aspect, m.depth, m.wrap); } }
            EditorGUILayout.BeginHorizontal(); if (GUILayout.Button("Select all its pieces")) Selection.objects = Pieces(m).Select(p => (Object)p.gameObject).ToArray(); if (GUILayout.Button("Remove it")) { foreach (var p in Pieces(m)) Undo.DestroyObjectImmediate(p.gameObject); GUIUtility.ExitGUI(); } EditorGUILayout.EndHorizontal();
        }

        static DecalMark[] Pieces(DecalMark m) => TsFind.All<DecalMark>(true).Where(d => d.group == m.group).ToArray();

        void OnSceneGUI()
        {
            if (!Placed || DecalPlacer.On) return; var m = M; var parent = m.transform.parent; var at = parent.TransformPoint(m.at); var n = parent.TransformDirection(m.normal).normalized; var rot = DecalPlacer.Facing(n, m.turn); float hs = HandleUtility.GetHandleSize(at);
            Vector3 r = rot * Vector3.right * m.size / 2, u = rot * Vector3.up * m.size * m.aspect / 2, lift = n * m.size * 0.01f; Handles.color = new Color(1f, 0.62f, 0.2f); Handles.DrawAAPolyLine(2.5f, at - r - u + lift, at + r - u + lift, at + r + u + lift, at - r + u + lift, at - r - u + lift);
            // slide over the surfaces
            EditorGUI.BeginChangeCheck(); var to = Handles.FreeMoveHandle(at + lift, hs * 0.09f, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck() && DecalPlacer.Under(HandleUtility.GUIPointToWorldRay(HandleUtility.WorldToGUIPoint(to)), Event.current.type, out var hit, out var hn)) { Remake(m, hit, hn, m.size, m.turn, m.aspect, m.depth, m.wrap); return; }
            // turn
            EditorGUI.BeginChangeCheck(); var q = Handles.Disc(rot, at + lift, n, Mathf.Max(m.size, m.size * m.aspect) * 0.62f, false, 0); if (EditorGUI.EndChangeCheck()) { float d = Vector3.SignedAngle(rot * Vector3.up, q * Vector3.up, -n); Remake(m, at, n, m.size, m.turn + d, m.aspect, m.depth, m.wrap); return; }
            // size, from the corner
            EditorGUI.BeginChangeCheck(); var c = at + r + u + lift; var c2 = Handles.Slider(c, (r + u).normalized, hs * 0.08f, Handles.CubeHandleCap, 0); if (EditorGUI.EndChangeCheck()) { float k = Mathf.Max(0.05f, Vector3.Dot(c2 - at, (r + u).normalized) / Mathf.Max(1e-4f, (r + u).magnitude)); Remake(m, at, n, m.size * k, m.turn, m.aspect, m.depth, m.wrap); }
        }

        /// <summary>Takes all its pieces away and places it again with the new settings (one Undo step).</summary>
        static void Remake(DecalMark m, Vector3 at, Vector3 normal, float size, float turn, float aspect, float depth, bool wrap)
        {
            var mat = m.GetComponent<MeshRenderer>() ? m.GetComponent<MeshRenderer>().sharedMaterial : null; if (!mat) return; var group = m.group; Undo.SetCurrentGroupName("Change decal"); int ug = Undo.GetCurrentGroup();
            foreach (var p in Pieces(m)) Undo.DestroyObjectImmediate(p.gameObject);
            var (om, oa, od, ow) = (DecalPlacer.Material, DecalPlacer.Aspect, DecalPlacer.Depth, DecalPlacer.Wrap); DecalPlacer.Material = mat; DecalPlacer.Aspect = aspect; DecalPlacer.Depth = depth; DecalPlacer.Wrap = wrap; DecalPlacer.ForceCell = m.cell; DecalPlacer.Fresh();
            GameObject first; try { first = DecalPlacer.Place(at, normal, size, turn, group); } finally { DecalPlacer.Material = om; DecalPlacer.Aspect = oa; DecalPlacer.Depth = od; DecalPlacer.Wrap = ow; DecalPlacer.ForceCell = -1; }
            Undo.CollapseUndoOperations(ug); if (first) Selection.activeGameObject = first; GUIUtility.ExitGUI();
        }
    }
}
