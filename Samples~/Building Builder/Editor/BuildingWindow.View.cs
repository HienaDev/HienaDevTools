using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>The building's own 3D view in the middle of the window, and the same tools in the Scene view.</summary>
    public partial class BuildingWindow
    {
        /// <summary>Where the tools draw and look: this window's view or a Scene view.</summary>
        public class View
        {
            public Rect rect; public Camera cam; public bool scene;
            public Ray Ray(Vector2 m) { if (scene) return HandleUtility.GUIPointToWorldRay(m); float k = EditorGUIUtility.pixelsPerPoint; return cam.ScreenPointToRay(new Vector3((m.x - rect.x) * k, (rect.yMax - m.y) * k, 0)); }
            public bool Gui(Vector3 w, out Vector2 g)
            {
                if (scene) { var c = Camera.current; g = HandleUtility.WorldToGUIPoint(w); return !c || c.WorldToViewportPoint(w).z > 0; }
                float k = EditorGUIUtility.pixelsPerPoint; var s = cam.WorldToScreenPoint(w); g = new Vector2(rect.x + s.x / k, rect.yMax - s.y / k); return s.z > 0;
            }
        }

        PreviewRenderUtility m_Pru; [SerializeField] Vector3 m_Pivot; [SerializeField] float m_Yaw = 30, m_Pitch = 45, m_Dist = 22; [SerializeField] bool m_Top; Mesh m_Ground; Material m_GroundMat;

        void Middle(Rect r)
        {
            var e = Event.current; var et = e.type; EditorGUI.DrawRect(r, new Color(0.08f, 0.085f, 0.1f));
            // the bar on top
            var bar = new Rect(r.x, r.y, r.width, 50); EditorGUI.DrawRect(bar, Panel); GUILayout.BeginArea(new Rect(bar.x + 6, bar.y + 4, bar.width - 12, 44)); GUILayout.BeginHorizontal();
            if (Pill("Build here", !m_InScene, "In this window's own view")) { m_InScene = false; SceneView.RepaintAll(); } if (Pill("In the Scene view", m_InScene, "The same tools in the Scene view, with the rest of your level")) { m_InScene = true; var sv = SceneView.lastActiveSceneView; if (sv) sv.Focus(); SceneView.RepaintAll(); }
            GUILayout.Space(10); if (!m_InScene) { if (Pill("3D", !m_Top && !m_Walk)) { m_Top = false; Walk(false); } if (Pill("From above (T)", m_Top && !m_Walk)) { m_Top = true; Walk(false); } if (Pill("Frame it (F)", false)) Frame(); GUILayout.Space(6); if (Pill("\u25B6 Walk (Tab)", m_Walk, "Walk round it as a person, to try the layout")) Walk(!m_Walk); if (Pill("\u2699", m_PersonPanel, "The person: their height and your player model (walk mode and P)", GUILayout.Width(22))) m_PersonPanel = !m_PersonPanel; if (Pill("Measure (M)", m_Measure)) m_Measure = !m_Measure; }
            GUILayout.FlexibleSpace(); using (new EditorGUI.DisabledScope(!m_B)) { if (GUILayout.Button(new GUIContent("↶ Undo", "Ctrl+Z"), EditorStyles.miniButton)) Undo.PerformUndo(); if (GUILayout.Button(new GUIContent("Redo ↷", "Ctrl+Y"), EditorStyles.miniButton)) Undo.PerformRedo(); } GUILayout.EndHorizontal(); GUILayout.Space(2); GUILayout.BeginHorizontal();
            GUILayout.Label("Show:", EditorStyles.miniLabel, GUILayout.Width(36)); if (Pill("Roof", !m_HideRoof, "Show or hide the roof (J)")) { m_HideRoof = !m_HideRoof; Floors(); } if (Pill("Outside walls", !m_HideOuter, "Show or hide the walls that face outside, to see in (N)")) { m_HideOuter = !m_HideOuter; Floors(); } if (Pill("Any floor", m_AnyFloor, "Select on whichever floor you click (it goes to that floor)")) m_AnyFloor = !m_AnyFloor;
            if (!m_InScene) { int lt = EditorGUILayout.Popup(m_Light, Lights, EditorStyles.miniPullDown, GUILayout.Width(74)); if (lt != m_Light) m_Light = lt; } if (Pill("Keys (F1)", m_ShowKeys)) m_ShowKeys = !m_ShowKeys;
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.EndArea();
            var area = new Rect(r.x, bar.yMax, r.width, r.height - bar.height);
            if (m_InScene) { GUI.Label(area, "Working in the Scene view.\nThe tools and keys work there the same way.", new GUIStyle(m_Big) { fontSize = 13, wordWrap = true }); return; }
            if (!m_B) { GUI.Label(area, "Press New on the left to start a building.", new GUIStyle(m_Big) { fontSize = 13 }); return; }
            if (m_InScene && m_Walk) Walk(false);
            if (m_Pru == null) { m_Pru = new PreviewRenderUtility(); m_Pru.camera.fieldOfView = 40; m_Pru.camera.nearClipPlane = 0.05f; m_Pru.camera.farClipPlane = 3000; m_Pru.camera.clearFlags = CameraClearFlags.SolidColor; m_Pru.camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f); }
            // a right click while dragging something new: stop, and nothing is made
            if (e.type == EventType.MouseDown && e.button == 1 && Busy && area.Contains(e.mousePosition)) { CancelDrag(); e.Use(); }
            if (m_Walk) { WalkLook(area, e); WalkCamera(m_Pru.camera); } else { Navigate(area, e); Place(m_Pru.camera); m_Pru.camera.fieldOfView = 40; }
            var view = new View { rect = area, cam = m_Pru.camera };
            if (e.type == EventType.Repaint)
            {
                m_Pru.BeginPreview(area, GUIStyle.none); if (m_Walk) WalkCamera(m_Pru.camera); else Place(m_Pru.camera); ApplyLight(m_Pru, m_Pru.camera);
                DrawGround(); foreach (var mr in m_B.GetComponentsInChildren<MeshRenderer>()) { var made = mr.GetComponentInParent<BuildingMade>(); if (m_HideAbove && made && made.level > m_Level) continue; if (Hidden(mr)) continue; var mf = mr.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; var mats = mr.sharedMaterials; for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) if (s < mats.Length && mats[s]) m_Pru.DrawMesh(mf.sharedMesh, mr.localToWorldMatrix, mats[s], s); }
                if (!m_Walk) { DrawPerson(m_Pru); DrawGhost(m_Pru); }
                m_Pru.Render(true, true); var tex = m_Pru.EndPreview(); GUI.DrawTexture(area, tex, ScaleMode.StretchToFill, false);
            }
            if (m_Walk) { var c = area.center; EditorGUI.DrawRect(new Rect(c.x - 1, c.y - 8, 2, 16), new Color(1, 1, 1, 0.7f)); EditorGUI.DrawRect(new Rect(c.x - 8, c.y - 1, 16, 2), new Color(1, 1, 1, 0.7f)); GUI.Label(new Rect(area.x + 8, area.y + 8, 600, 20), "WALKING: WASD, drag to look, Space jump, Shift run, Tab or Esc to stop", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Accent } }); }
            else { GUI.BeginClip(new Rect(0, 0, position.width, position.height)); Work(view, e); GUI.EndClip(); Overlays(area, e); }
            // what's under the mouse, in words
            GUI.Label(new Rect(area.x + 8, area.yMax - 22, area.width - 16, 18), Building.LevelName(m_Level) + "   •   " + Tools.First(q => q.tool == m_Tool).name + "   •   snap " + (m_B.cell * Snaps[m_Snap]).ToString("0.###") + "   •   " + m_Status, new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1, 1, 1, 0.7f) } });
            // (the tools use up the event, so look at what it was: a drag shows what you'll get while you drag)
            if ((area.Contains(e.mousePosition) || GUIUtility.hotControl != 0) && (et == EventType.MouseMove || et == EventType.MouseDrag || et == EventType.MouseDown || et == EventType.MouseUp)) Repaint();
        }
        string m_Status = ""; Vector2 m_RightFrom; bool m_RightMoved;

        void DrawGround()
        {
            if (m_Level < 0) return; if (!m_Ground) { m_Ground = new Mesh { hideFlags = HideFlags.HideAndDontSave }; float s = 200; m_Ground.vertices = new[] { new Vector3(-s, 0, -s), new Vector3(-s, 0, s), new Vector3(s, 0, s), new Vector3(s, 0, -s) }; m_Ground.triangles = new[] { 0, 1, 2, 0, 2, 3 }; m_Ground.normals = Enumerable.Repeat(Vector3.up, 4).ToArray(); }
            if (!m_GroundMat) { m_GroundMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave }; m_GroundMat.SetColor(m_GroundMat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", new Color(0.3f, 0.32f, 0.3f)); }
            m_Pru.DrawMesh(m_Ground, Matrix4x4.TRS(m_B.transform.position + Vector3.down * 0.02f, m_B.transform.rotation, Vector3.one), m_GroundMat, 0);
        }
        void Place(Camera c)
        {
            var rot = Quaternion.Euler(m_Top ? 89.9f : Mathf.Clamp(m_Pitch, -5, 89.9f), m_Yaw, 0); var piv = m_B ? m_B.transform.TransformPoint(m_Pivot) + Vector3.up * m_B.Y(m_Level) : m_Pivot; c.transform.rotation = rot; c.transform.position = piv - rot * Vector3.forward * m_Dist;
        }
        void Navigate(Rect area, Event e)
        {
            int id = GUIUtility.GetControlID("BuildingNav".GetHashCode(), FocusType.Passive); bool over = area.Contains(e.mousePosition);
            if (e.type == EventType.MouseDown && over && (e.button == 1 || e.button == 2)) { GUIUtility.hotControl = id; m_RightFrom = e.mousePosition; m_RightMoved = false; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && (e.mousePosition - m_RightFrom).magnitude > 4) m_RightMoved = true;
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id && e.button == 1 && !m_RightMoved && m_B) { GUIUtility.hotControl = 0; e.Use(); var keep = m_V; m_V = new View { rect = area, cam = m_Pru.camera }; Context(e.mousePosition); m_V = keep; return; }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                if (e.button == 2 || e.shift) { var rot = Quaternion.Euler(m_Top ? 90 : m_Pitch, m_Yaw, 0); var d = (rot * Vector3.left * e.delta.x + rot * Vector3.up * e.delta.y) * m_Dist * 0.0022f; m_Pivot += m_B ? m_B.transform.InverseTransformVector(d) : d; }
                else { m_Yaw += e.delta.x * 0.4f; m_Pitch = Mathf.Clamp(m_Pitch + e.delta.y * 0.4f, -5, 89.9f); m_Top = false; }
                e.Use(); Repaint();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
            if (e.type == EventType.ScrollWheel && over) { m_Dist = Mathf.Clamp(m_Dist * (e.delta.y > 0 ? 1.12f : 0.89f), 1.5f, 600); e.Use(); Repaint(); }
            if (e.type == EventType.ContextClick && over) e.Use();
        }
        /// <summary>Looks at the whole building (or the middle of the grid when it's empty).</summary>
        void Frame()
        {
            if (!m_B) return; var fl = m_B.FloorAt(m_Level); var pts = new List<Vector2>(); foreach (var f in m_B.floors) { foreach (var a in f.areas) { pts.Add(a.min); pts.Add(a.max); } foreach (var w in f.walls) { pts.Add(w.a); pts.Add(w.b); } }
            if (pts.Count == 0) { m_Pivot = new Vector3(4, 0, 4); m_Dist = 22; return; } var lo = pts.Aggregate(Vector2.Min); var hi = pts.Aggregate(Vector2.Max); var c = (lo + hi) / 2; m_Pivot = new Vector3(c.x, 0, c.y); m_Dist = Mathf.Clamp((hi - lo).magnitude * 1.3f + 6, 6, 400); Repaint();
        }

        // ------------------------------------------------------------------ the Scene view

        void Scene(SceneView sv)
        {
            if (!m_B || !m_InScene) return; var e = Event.current; if (e.type == EventType.KeyDown && Keys(e)) { e.Use(); Repaint(); sv.Repaint(); return; }
            Handles.BeginGUI(); GUI.Label(new Rect(56, 8, 760, 22), "Building Builder: " + Building.LevelName(m_Level) + "   •   " + Tools.First(q => q.tool == m_Tool).name + "   •   " + m_Status, new GUIStyle(EditorStyles.helpBox) { fontSize = 12 }); Handles.EndGUI();
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID("BuildingTool".GetHashCode(), FocusType.Passive));
            if (e.type == EventType.MouseDown && e.button == 1 && Busy) { CancelDrag(); e.Use(); return; }
            DrawGhostScene(); var et = e.type; Handles.BeginGUI(); Work(new View { scene = true, rect = sv.position }, e); Handles.EndGUI(); if (et == EventType.MouseMove || et == EventType.MouseDrag || et == EventType.MouseDown || et == EventType.MouseUp) sv.Repaint();
        }
    }
}
