using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>
    /// A note or a TODO pinned to an object (right-click it in the Hierarchy > Add Note), or placed anywhere in the Scene
    /// view (Notes & TODO > "+ Note in the scene"). It's saved in the scene or prefab, so git shares it with the team; it
    /// shows in the Hierarchy, as a sticky card in the Scene view and in Tools > Notes & TODO. Builds leave it out.
    /// </summary>
    [AddComponentMenu("Dev Tools/Note")]
    [DisallowMultipleComponent]
    public class DevNote : MonoBehaviour
    {
        public enum Kind { Note, Todo }
        public enum Priority { Low, Normal, High }
        [System.Serializable] public class Step { public string text = ""; public bool done; }

        public Kind kind = Kind.Todo;
        [TextArea(2, 8)] public string text = "";
        [Tooltip("Who it's for: Art, Code, Sound, a name... (free text)")] public string forWho = "";
        public Priority priority = Priority.Normal;
        public bool done;
        public Color color = new Color(1f, 0.82f, 0.25f);
        // (added later: a checklist inside the note, and the Scene view camera it was written from)
        public List<Step> steps = new List<Step>();
        public bool hasView;
        public Vector3 viewPivot; public Quaternion viewRotation = Quaternion.identity; public float viewSize = 10f;

        public int StepsDone { get { int n = 0; foreach (var s in steps) if (s.done) n++; return n; } }
    }
}
