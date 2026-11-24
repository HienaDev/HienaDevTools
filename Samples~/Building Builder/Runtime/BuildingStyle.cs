using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A building's look in one asset: its materials and sizes. The Building Builder saves and uses them, so a whole
    /// building can switch from "Medieval" to "Office" in one click.</summary>
    [CreateAssetMenu(menuName = "Dev Tools/Building Style", fileName = "Style", order = 401)]
    public class BuildingStyle : ScriptableObject
    {
        public List<Material> materials = new List<Material>(); public float floorHeight = 3, wallThickness = 0.15f, slabThickness = 0.2f, textureSize = 2; public int roundSides = 6;
        public Building.RoofKind roof; public float roofPitch = 35, roofOverhang = 0.3f; public Building.Finish finish; public bool doorLeaves = true, psx; public float psxWobble = 0.5f;
    }

}
