using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Walk mode: a person you steer in this window's view (WASD, the mouse to look, Space to jump, Shift to run),
    /// bumping into walls and climbing stairs and ladders, to try the layout without pressing Play.</summary>
    public partial class BuildingWindow
    {
        [SerializeField] bool m_Walk; Vector3 m_Feet, m_Vel; float m_WYaw, m_WPitch; readonly HashSet<KeyCode> m_Held = new HashSet<KeyCode>(); double m_WLast;
        const float Radius = 0.3f, StepUp = 0.38f;

        void Walk(bool on)
        {
            if (!m_B) return; m_Walk = on; m_Held.Clear(); EditorApplication.update -= WalkTick; ShutDoors();
            if (on)
            {
                // start where the person stands, or in the middle of the floor you're on
                var at = m_PersonAt.x > -0.5f || m_PersonAt.y > -0.5f ? m_PersonAt : (Vector2)FloorMiddle(); m_Feet = Tr.TransformPoint(new Vector3(at.x, m_B.Y(m_Level) + Surface(at) + 0.01f, at.y)); m_Vel = Vector3.zero; m_WYaw = m_Yaw; m_WPitch = 0; m_WLast = EditorApplication.timeSinceStartup;
                Physics.SyncTransforms(); EditorApplication.update += WalkTick; m_Status = "walking: WASD, mouse to look, Space jump, Shift run, Tab or Esc to stop";
            }
            Repaint();
        }
        Vector2 FloorMiddle() { var fl = m_B.FloorAt(m_Level); if (fl == null || fl.areas.Count == 0) return Vector2.zero; var a = fl.areas.First(q => !q.hole); return (a.min + a.max) / 2; }
        bool WalkKey(Event e, bool down)
        {
            if (down && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Tab)) { Walk(false); return true; }
            if (e.keyCode == KeyCode.W || e.keyCode == KeyCode.A || e.keyCode == KeyCode.S || e.keyCode == KeyCode.D || e.keyCode == KeyCode.Space || e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.Q || e.keyCode == KeyCode.E)
            { if (down) { if (e.keyCode == KeyCode.Space && Grounded()) m_Vel.y = 5f; m_Held.Add(e.keyCode); } else m_Held.Remove(e.keyCode); return true; }
            return down; // (other keys do nothing while walking)
        }
        float EyeHeight => Mathf.Max(0.5f, m_PersonHeight - 0.15f);
        bool Grounded() => Cast(m_Feet + Vector3.up * StepUp, Vector3.down, StepUp + 0.06f, out _);
        readonly RaycastHit[] m_Hits = new RaycastHit[32];
        bool Cast(Vector3 from, Vector3 dir, float dist, out RaycastHit hit)
        {
            // one physics query (much faster than asking every collider), leaving out doors (they swing open) and triggers
            hit = default; float best = dist; bool any = false; int n = Physics.RaycastNonAlloc(new Ray(from, dir), m_Hits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) { var h = m_Hits[i]; if (h.distance >= best || h.collider.GetComponentInParent<BuildingDoor>()) continue; best = h.distance; hit = h; any = true; }
            return any;
        }
        List<Collider> m_Cols; double m_ColsAt; IEnumerable<Collider> Colliders() { if (m_Cols == null || EditorApplication.timeSinceStartup - m_ColsAt > 1) { m_ColsAt = EditorApplication.timeSinceStartup; m_Cols = BbFind.All<Collider>().Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && !c.GetComponentInParent<BuildingDoor>()).ToList(); } return m_Cols; }

        void WalkTick()
        {
            if (!m_Walk || !m_B) { EditorApplication.update -= WalkTick; return; }
            double now = EditorApplication.timeSinceStartup; float dt = Mathf.Min(0.05f, (float)(now - m_WLast)); m_WLast = now; if (dt <= 0) return;
            float speed = m_Held.Contains(KeyCode.LeftShift) ? m_WalkSpeed * 2 : m_WalkSpeed; var fwd = Quaternion.Euler(0, m_WYaw, 0) * Vector3.forward; var right = Quaternion.Euler(0, m_WYaw, 0) * Vector3.right;
            var move = Vector3.zero; if (m_Held.Contains(KeyCode.W) || m_Held.Contains(KeyCode.UpArrow)) move += fwd; if (m_Held.Contains(KeyCode.S) || m_Held.Contains(KeyCode.DownArrow)) move -= fwd; if (m_Held.Contains(KeyCode.D)) move += right; if (m_Held.Contains(KeyCode.A)) move -= right;
            if (m_Held.Contains(KeyCode.LeftArrow)) m_WYaw -= 120 * dt; if (m_Held.Contains(KeyCode.RightArrow)) m_WYaw += 120 * dt;
            if (move.sqrMagnitude > 1) move.Normalize(); var step = move * speed * dt;
            // walls: try the move at knee, waist and head height, and slide along what's in the way
            for (int k = 0; k < 2 && step.sqrMagnitude > 1e-8f; k++) foreach (float hgt in new[] { StepUp + 0.05f, 0.9f, EyeHeight }) { if (Cast(m_Feet + Vector3.up * hgt, step.normalized, step.magnitude + Radius, out var h)) { var n = h.normal; n.y = 0; if (n.sqrMagnitude < 1e-4f) continue; n.Normalize(); step -= n * Vector3.Dot(step, n); step += n * Mathf.Max(0, Radius - (h.distance)) * 0.5f; } }
            m_Feet += step; WalkDoors(dt);
            // ladders: walking into one climbs it
            bool climbing = false; if (move.sqrMagnitude > 0.01f) { var lp = Tr.InverseTransformPoint(m_Feet); var u = new Vector2(lp.x, lp.z); foreach (var f in m_B.floors) foreach (var s in f.stairs.Where(s => s.kind == Building.StairKind.Ladder)) { var r = BuildingMaker.Footprint(s); r.xMin -= 0.3f; r.yMin -= 0.3f; r.xMax += 0.3f; r.yMax += 0.3f; float y0 = m_B.Y(f.level) + s.lift; if (r.Contains(u) && lp.y >= y0 - 0.2f && lp.y < y0 + s.rise * m_B.HeightOf(f.level) + 0.3f) { climbing = true; m_Vel.y = 2.5f * Mathf.Sign(Vector3.Dot(move, fwd) + 0.01f); } } }
            // falling and standing, up steps no higher than a stair
            if (!climbing) m_Vel.y -= 20 * dt; float fall = m_Vel.y * dt; if (fall < 0 && Cast(m_Feet + Vector3.up * StepUp, Vector3.down, StepUp - fall + 0.05f, out var land)) { m_Feet.y = land.point.y; m_Vel.y = 0; fall = 0; } m_Feet.y += fall;
            // (the ray starts a step up and reaches as far as this frame's fall, so fast falls still land)
            if (m_Vel.y <= 0 && Cast(m_Feet + Vector3.up * StepUp, Vector3.down, StepUp + 0.1f - m_Vel.y * dt, out var g)) { m_Feet.y = g.point.y; m_Vel.y = 0; }
            if (m_Vel.y > 0 && Cast(m_Feet + Vector3.up * EyeHeight, Vector3.up, 0.2f, out _)) m_Vel.y = 0;
            if (m_Feet.y < m_B.transform.position.y - 60) Walk(false);
            if (now - m_WPaint > 1 / 60.0) { m_WPaint = now; Repaint(); }
        }
        double m_WPaint;
        // doors swing open as you come near (as they do in the game) and close behind you; put back when you stop
        class Swing { public Quaternion shut; public Vector3 middle; public float t, dir = 1; }
        readonly Dictionary<BuildingDoor, Swing> m_Swings = new Dictionary<BuildingDoor, Swing>();
        void WalkDoors(float dt)
        {
            bool moved = false;
            if (m_DoorList == null || EditorApplication.timeSinceStartup - m_DoorsAt > 1) { m_DoorsAt = EditorApplication.timeSinceStartup; m_DoorList = m_B.GetComponentsInChildren<BuildingDoor>(); }
            foreach (var d in m_DoorList)
            {
                if (!d) continue;
                if (!m_Swings.TryGetValue(d, out var s)) { var r = d.GetComponentInChildren<Renderer>(); m_Swings[d] = s = new Swing { shut = d.transform.localRotation, middle = r ? r.bounds.center : d.transform.position }; }
                var to = s.middle - m_Feet; bool near = new Vector2(to.x, to.z).magnitude < Mathf.Max(2f, d.near) && Mathf.Abs(to.y - 1f) < 1.8f;
                float n = Mathf.MoveTowards(s.t, near ? 1 : 0, dt * Mathf.Max(0.5f, d.speed) * 1.5f); if (Mathf.Approximately(n, s.t)) continue;
                // it swings away from you (which way is chosen as it starts to open)
                if (s.t <= 1e-4f && near) { var side = Vector3.Dot(m_Feet - d.transform.position, d.transform.parent ? d.transform.parent.rotation * s.shut * Vector3.forward : Vector3.forward); s.dir = side > 0 ? 1 : -1; }
                s.t = n; moved = true; float ang = d.bothWays ? Mathf.Abs(d.angle) * s.dir : d.angle; d.transform.localRotation = s.shut * Quaternion.Euler(0, Mathf.SmoothStep(0, ang, s.t), 0);
            }
            if (moved) Physics.SyncTransforms();
        }
        BuildingDoor[] m_DoorList; double m_DoorsAt;
        void ShutDoors() { foreach (var kv in m_Swings) if (kv.Key) kv.Key.transform.localRotation = kv.Value.shut; m_Swings.Clear(); Physics.SyncTransforms(); }
        void WalkLook(Rect area, Event e)
        {
            int id = GUIUtility.GetControlID("BuildingWalk".GetHashCode(), FocusType.Keyboard); if (e.type == EventType.MouseDown && area.Contains(e.mousePosition)) { GUIUtility.hotControl = id; GUIUtility.keyboardControl = id; e.Use(); }
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) { m_WYaw += e.delta.x * 0.25f; m_WPitch = Mathf.Clamp(m_WPitch + e.delta.y * 0.25f, -85, 85); e.Use(); Repaint(); }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
        }
        void WalkCamera(Camera c) { c.transform.position = m_Feet + Vector3.up * EyeHeight; c.transform.rotation = Quaternion.Euler(m_WPitch, m_WYaw, 0); c.fieldOfView = 70; }
    }
}
