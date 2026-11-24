using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Marks an object as a decal placed with Texture Studio, so new decals don't stick to it. Does nothing in the game.</summary>
    [AddComponentMenu("")]
    public class DecalMark : MonoBehaviour
    {
        // how it was placed (in its parent's space), so it can be moved, turned and sized again afterwards
        [HideInInspector] public string group = ""; [HideInInspector] public Vector3 at, normal, up; [HideInInspector] public float size = 1, turn, aspect = 1, depth = 0.35f; [HideInInspector] public bool wrap; [HideInInspector] public int cell = -1;
    }
}
