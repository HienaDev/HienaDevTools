using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Something to build with: your own model, or a simple shape the Building Builder makes (a box, a pillar, a
    /// wedge, stairs, an arch), with its material, collider and how many grid squares it takes.</summary>
    [CreateAssetMenu(menuName = "Dev Tools/Building Part", fileName = "Part", order = 400)]
    public class BuildPart : ScriptableObject
    {
        public enum Shape { MyModel, Box, Pillar, Wedge, Stairs, Arch, Beam, Fence }
        public enum Collide { Box, Exact, None }
        public enum Sits { Middle, Edge, Corner, Ceiling }
        public string category = "Props";
        public Shape shape = Shape.Box;
        [Tooltip("Your model or prefab (for My model)")] public GameObject model;
        [Tooltip("Width, height, depth (units). For your model it's worked out from it")] public Vector3 size = new Vector3(1, 1, 1);
        public Material material;
        public Collide collide = Collide.Box;
        [Tooltip("Where it snaps: the middle of a square, against a wall, a corner, or hanging from the ceiling")] public Sits sits = Sits.Middle;
        [Tooltip("Steps for Stairs")] public int steps = 12;
        [Tooltip("Faces round its sides (pillars, arches); 0 uses the building's setting")] public int sides;
    }
}
