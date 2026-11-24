using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Moss, dirt or anything else painted onto an object with Texture Studio's Paint page. It's a thin extra surface
    /// on the object (its child); the object's own material isn't changed. Does nothing in the game.</summary>
    [AddComponentMenu("")]
    public class PaintedLayer : MonoBehaviour
    {
        [System.Serializable] public struct Dab { public Vector3 at; public float radius; public int level, firstVertex, vertices, firstIndex, indices; }
        [HideInInspector] public List<Dab> dabs = new List<Dab>();
    }
}
