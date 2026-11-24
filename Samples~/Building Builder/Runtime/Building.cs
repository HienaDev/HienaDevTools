using System;
using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A building made with the Building Builder (Tools > Building Builder). This only holds the plan, in units
    /// (metres): floors made of rectangles, walls as lines with doors and windows in them, stairs and your own parts. The
    /// builder makes the meshes, colliders and props as children.</summary>
    [AddComponentMenu("Dev Tools/Building")]
    public class Building : MonoBehaviour, ISerializationCallbackReceiver
    {
        public enum OpeningKind { Door, Window, Arch, Gap }
        public enum StairKind { Straight, TurnLeft, TurnRight, UTurn, Ramp, Spiral, Ladder }
        public enum RoofKind { None, Flat, Parapet, Gable, Hip, Shed }
        /// <summary>Which way a door swings: away from whoever comes (both ways), or only to its front or back side.</summary>
        public enum DoorSwing { BothWays, ToTheFront, ToTheBack }
        /// <summary>What's under a stair: solid, open (treads on stringers) or a cupboard with a door (when it runs along a wall).</summary>
        public enum StairUnder { Solid, Open, Cupboard }
        /// <summary>The pieces the builder makes that can be swapped for your own model (stretched to fit) or given their own material.</summary>
        public enum PieceKind { Step, StepTop, RailPost, Handrail, DoorLeaf, Window, Glass, Column, Beam, Gutter, LadderRung, Roof }
        [Serializable] public class Swap { public PieceKind kind; [Tooltip("Your model, put where each of these goes (stretched to the same size when Fit is on)")] public GameObject model; [Tooltip("Or just another material for the one the builder makes")] public Material material; [Tooltip("Stretch the model to the piece's size; off keeps its own size")] public bool fit = true; }
        /// <summary>What holds a railing's handrail up.</summary>
        public enum RailSupport { Posts, Bars, Panel, Glass, None }
        /// <summary>Which sides of a stair get a railing. Auto: both, except a side that runs along a wall.</summary>
        public enum RailSides { Auto, Both, Left, Right, None }
        /// <summary>How a railing looks: its supports, rails, size and your own models for its posts and handrail.</summary>
        [Serializable] public class RailStyle
        {
            [Tooltip("This one has its own look (off: it looks like the building's railings)")] public bool own;
            [Tooltip("How high its handrail is")] public float height = 0.95f;
            [Tooltip("What holds the handrail up")] public RailSupport support = RailSupport.Posts;
            [Tooltip("The gap between posts or bars")] public float spacing = 1f;
            [Tooltip("How thick posts are")] public float postSize = 0.05f;
            [Tooltip("0 square, 3 and up: that many faces")] public int round;
            public bool handrail = true; [Tooltip("A second rail half way down")] public bool midRail = true; [Tooltip("A rail just above the floor")] public bool bottomRail;
            [Tooltip("Your model for each post or bar (stretched to fit when Fit is on)")] public GameObject postModel;
            [Tooltip("Your model for the handrail (stretched along it when Fit is on)")] public GameObject handrailModel;
            public bool fit = true; [Tooltip("Its material slot (-1: the trim's)")] public int mat = -1;
            public RailStyle Copy() => (RailStyle)MemberwiseClone();
        }
        [Flags] public enum Finish { None = 0, Skirting = 1, Crown = 2, Wainscot = 4 }

        /// <summary>A rectangle of floor (or a hole in it). Where several overlap, the last one wins.</summary>
        [Serializable] public class Area { public string name = ""; public Vector2 min, max; public bool hole, roof; public int top = -1, under = -1, roofMat = -1; [Tooltip("Raised (or sunk) from the floor: split levels, a stage, a pit")] public float lift; [Tooltip("Railings round its open edges (when the floor has railings on)")] public RailStyle railing = new RailStyle();
            [Tooltip("Its outline when it isn't a rectangle (in order round it); empty: the rectangle min to max")] public List<Vector2> shape = new List<Vector2>();
            [Tooltip("A hole: railings round it (when the floor has railings on)")] public bool rails = true;
            [Tooltip("Filled solid down to the floor below (under a landing or a bridge)")] public bool solidBelow;
            [Tooltip("Posts under it down to the ground (a balcony)")] public bool posts; [Tooltip("The gap between the posts")] public float postGap = 2f;
            public bool IsShape => shape != null && shape.Count >= 3;
            /// <summary>Its corners, in order (the rectangle's four when it has no shape).</summary>
            public List<Vector2> Corners() { if (IsShape) return new List<Vector2>(shape); var lo = Vector2.Min(min, max); var hi = Vector2.Max(min, max); return new List<Vector2> { lo, new Vector2(hi.x, lo.y), hi, new Vector2(lo.x, hi.y) }; }
            /// <summary>Sets its outline (min and max become the box round it).</summary>
            public void SetShape(List<Vector2> pts) { shape = pts; if (pts.Count == 0) return; var lo = pts[0]; var hi = pts[0]; foreach (var p in pts) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); } min = lo; max = hi; }
            /// <summary>Moves it (min, max and its outline) through f.</summary>
            public void Move(Func<Vector2, Vector2> f) { if (IsShape) { SetShape(shape.ConvertAll(p => f(p))); return; } var a = f(min); var b = f(max); min = Vector2.Min(a, b); max = Vector2.Max(a, b); }
            /// <summary>Whether u is inside it.</summary>
            public bool Contains(Vector2 u) { if (!IsShape) { var lo = Vector2.Min(min, max); var hi = Vector2.Max(min, max); return u.x >= lo.x && u.x <= hi.x && u.y >= lo.y && u.y <= hi.y; } bool c = false; for (int i = 0, j = shape.Count - 1; i < shape.Count; j = i++) { var a = shape[i]; var b = shape[j]; if ((a.y > u.y) != (b.y > u.y) && u.x < (b.x - a.x) * (u.y - a.y) / (b.y - a.y) + a.x) c = !c; } return c; }
            public float AreaSize() { var c = Corners(); float s = 0; for (int i = 0; i < c.Count; i++) { var a = c[i]; var b = c[(i + 1) % c.Count]; s += a.x * b.y - b.x * a.y; } return Mathf.Abs(s) / 2; }
        }
        /// <summary>A door, window or arch in a wall: where its middle is along the wall, how wide, from and to what height.</summary>
        [Serializable] public class Opening { public OpeningKind kind; public float at, width = 1, bottom, top = 2.2f; public int sides = 6; [Tooltip("A frame round it")] public bool frame = true; [Tooltip("Glass in a window, a door leaf in a door")] public bool fill = true; [HideInInspector] public bool opensBack; [Tooltip("Which way the door swings")] public DoorSwing swing; [Tooltip("Hinged on its other side")] public bool hingeOther; [Tooltip("Two doors, one hinged at each side")] public bool doubleDoor;
            [Tooltip("Your own model for this one: the door that swings (a door), or what fills the hole (a window, arch or gap)")] public GameObject model; [Tooltip("Stretch your model to the hole's size")] public bool fit = true; [Tooltip("Its frame's material slot (-1: the trim's)")] public int frameMat = -1; [Tooltip("Its door's material slot (-1: the usual)")] public int leafMat = -1; }
        [Serializable] public class Wall { public Vector2 a, b; public float height = -1, bottom, thickness = -1; public int front = -1, back = -1; public List<Opening> openings = new List<Opening>(); [Tooltip("Bows out (to its front) by this much in the middle: a curved wall")] public float bend; public Finish frontFinish, backFinish;
            /* (from the first version) */ [HideInInspector] public Vector2Int at; [HideInInspector] public bool alongZ; [HideInInspector] public int kind; public float Length => (b - a).magnitude; }
        [Serializable] public class Stair { public StairKind kind; public Vector2 at; public float turn, width = 1, length = 3; [Tooltip("1 goes up a whole floor, 0.5 half way")] public float rise = 1; public int steps = -1, mat = -1; [Tooltip("The tops of the steps (-1: the same)")] public int topMat = -1; [HideInInspector] public bool rails = true; [Tooltip("Starts this high above the floor: on a landing or other stairs")] public float lift;
            [Tooltip("Which sides get a railing. Auto: both, except along a wall")] public RailSides railSides; public RailStyle railing = new RailStyle();
            [Tooltip("What's under it: solid, open, or a cupboard with a door (needs a wall along one side)")] public StairUnder under; }
        [Serializable] public class Column { public Vector2 at; public float size = 0.3f; [Tooltip("0 square, more for round")] public int sides; public int mat = -1; public float lift; }
        [Serializable] public class Beam { public Vector2 a, b; public float width = 0.2f, depth = 0.3f; public int mat = -1; }
        [Serializable] public class Piece { public string part = ""; public Vector2 at; public float turn, lift; public int mat = -1; public Vector3 scale = Vector3.one; }
        [Serializable] public class Floor { public int level; public string name = ""; [Tooltip("Its own height, or 0 for the building's")] public float height; public List<Area> areas = new List<Area>(); public List<Wall> walls = new List<Wall>(); public List<Stair> stairs = new List<Stair>(); public List<Piece> pieces = new List<Piece>(); public List<Column> columns = new List<Column>(); public List<Beam> beams = new List<Beam>();
            [Tooltip("Railings where you could fall: floor edges with no wall, holes, balconies")] public bool rails;
            public RoofKind roof; [Tooltip("Degrees")] public float roofPitch = 35; public float roofOverhang = 0.3f; public int roofMat = -1; public bool gutters;
            [Tooltip("A floor inside the roof you can walk on (stairs from this floor reach it), with low walls round it")] public bool attic; [Tooltip("How high the low walls round the attic are")] public float atticWalls = 0.8f; [Tooltip("Windows in the gable ends")] public bool roofWindows;
            /* (from the first version) */ [HideInInspector] public List<Tile> tiles = new List<Tile>(); }
        [Serializable] public class Tile { public Vector2Int at; public int top = -1, under = -1; public bool roof; public int roofMat = -1; }

        [Tooltip("The grid square (units)")] public float cell = 1f;
        [Tooltip("From one floor to the next (units)")] public float floorHeight = 3f;
        public float wallThickness = 0.15f, slabThickness = 0.2f;
        [Tooltip("Units one texture covers")] public float textureSize = 2f;
        [Tooltip("Faces on round things (arches, pillars): fewer is more low-poly")] public int roundSides = 6;
        [Tooltip("0 floor, 1 walls, 2 ceiling, 3 stairs and trims, then any others")] public List<Material> materials = new List<Material>();
        [Tooltip("The old-console look: wobbly vertices, pixel textures, light per corner")] public bool psx;
        [Range(0, 1), Tooltip("How much corners jump to the old screen's few pixels")] public float psxWobble = 0.5f;
        [Range(0, 1), Tooltip("Textures that slide on big faces, like the old hardware (big faces are cut up so it stays pleasant)")] public float psxSwim = 0.35f;
        [Range(0, 1), Tooltip("Fewer colours, with a dotted pattern between them")] public float psxColours = 0.5f;
        [Header("Finishes")] public float skirtingHeight = 0.12f, crownHeight = 0.1f, wainscotHeight = 0.9f, trimWidth = 0.08f; public int trimMat = 3, wainscotMat = -1;
        [Tooltip("New doors get a door leaf that opens in the game")] public bool doorLeaves = true;
        [Header("Ground")] [Tooltip("Walls and floor go this far down, more where the ground is lower")] public float foundation; public int foundationMat = -1;
        [Tooltip("Stairs get a smooth slope to walk on (their steps have no collider)")] public bool smoothStairs = true;
        [Tooltip("How railings look (stairs, balconies, floor edges), unless one has its own")] public RailStyle railing = new RailStyle();
        public RailStyle RailFor(RailStyle own) => own != null && own.own ? own : railing ?? (railing = new RailStyle());
        [Tooltip("Your own models or materials for the pieces the builder makes")] public List<Swap> swaps = new List<Swap>();
        public Swap SwapFor(PieceKind k) { foreach (var s in swaps) if (s.kind == k && (s.model || s.material)) return s; return null; }
        public List<Floor> floors = new List<Floor>();

        public Floor FloorAt(int level, bool make = false)
        {
            foreach (var f in floors) if (f.level == level) return f; if (!make) return null;
            var n = new Floor { level = level }; floors.Add(n); floors.Sort((a, b) => a.level.CompareTo(b.level)); return n;
        }
        public static string LevelName(int level) => level == 0 ? "Ground floor" : level > 0 ? "Floor " + level : "Basement " + (-level);
        public float HeightOf(int level) { var f = FloorAt(level); return f != null && f.height > 0.01f ? f.height : floorHeight; }
        public float Y(int level) { float y = 0; if (level >= 0) for (int k = 0; k < level; k++) y += HeightOf(k); else for (int k = level; k < 0; k++) y -= HeightOf(k); return y; }
        public int LevelAt(float y) { int l = 0; if (y >= 0) { while (l < 200 && y >= Y(l + 1) - 1e-4f) l++; } else { while (l > -200 && y < Y(l) - 1e-4f) l--; } return l; }
        public int LevelNear(float y) { int l = LevelAt(y); return Mathf.Abs(Y(l + 1) - y) < Mathf.Abs(Y(l) - y) ? l + 1 : l; }

        public void OnBeforeSerialize() { }
        /// <summary>Plans from the first version (squares and walls on grid lines) are turned into the new kind.</summary>
        public void OnAfterDeserialize()
        {
            foreach (var f in floors) foreach (var st in f.stairs) if (!st.rails) { st.rails = true; st.railSides = RailSides.None; }
            foreach (var f in floors) foreach (var w in f.walls) foreach (var o in w.openings) if (o.opensBack) { o.opensBack = false; o.swing = DoorSwing.ToTheBack; }
            bool old = false; foreach (var f in floors) { if (f.tiles.Count > 0) old = true; foreach (var w in f.walls) if (w.a == w.b) old = true; foreach (var p in f.pieces) if (p.part.StartsWith("builtin:")) old = true; } if (!old) return;
            foreach (var f in floors)
            {
                foreach (var t in f.tiles) f.areas.Add(new Area { min = (Vector2)t.at * cell, max = ((Vector2)t.at + Vector2.one) * cell, top = t.top, under = t.under, roof = t.roof, roofMat = t.roofMat }); f.tiles.Clear();
                foreach (var w in f.walls) if (w.a == w.b) { w.a = (Vector2)w.at * cell; w.b = w.a + (w.alongZ ? Vector2.up : Vector2.right) * cell; if (w.kind > 0) w.openings.Add(new Opening { kind = w.kind == 1 ? OpeningKind.Door : w.kind == 2 ? OpeningKind.Window : w.kind == 4 ? OpeningKind.Arch : OpeningKind.Gap, at = cell / 2, width = Mathf.Min(1, cell * 0.8f), bottom = w.kind == 2 ? 1 : 0, top = w.kind == 2 ? 2.1f : w.kind == 3 ? 0 : 2.2f }); if (w.kind == 3) { w.height = 1.1f; w.openings.Clear(); } }
                foreach (var p in f.pieces.ToArray()) if (p.part == "builtin:stairs" || p.part == "builtin:ramp") { f.pieces.Remove(p); f.stairs.Add(new Stair { kind = p.part == "builtin:ramp" ? StairKind.Ramp : StairKind.Straight, at = p.at * cell, turn = p.turn, width = cell * 0.9f, length = (p.part == "builtin:ramp" ? 3 : 2) * cell, mat = p.mat }); } else p.at *= cell;
            }
        }
    }

    /// <summary>Marks what the Building Builder made, so it can be made again. Don't put your own things under it.</summary>
    [AddComponentMenu("")]
    public class BuildingMade : MonoBehaviour { public int level; public int piece = -1; [Tooltip("A door leaf: its wall and opening")] public int wall = -1, opening = -1; }
}
