using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A room saved from a building (its floor, walls, doors, windows, stairs and parts), to stamp again.</summary>
    [CreateAssetMenu(menuName = "Dev Tools/Building Room", fileName = "Room", order = 402)]
    public class BuildingRoom : ScriptableObject
    {
        public Vector2 size; public List<Building.Area> areas = new List<Building.Area>(); public List<Building.Wall> walls = new List<Building.Wall>(); public List<Building.Stair> stairs = new List<Building.Stair>(); public List<Building.Piece> pieces = new List<Building.Piece>(); public List<Building.Column> columns = new List<Building.Column>();
    }
}
