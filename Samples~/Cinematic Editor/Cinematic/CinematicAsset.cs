using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A cinematic saved to a file in the project, so it's safe even if its scene object is deleted, and can be
/// loaded into any scene. Everything is kept as it is (shots, timings, motions, looks, overlays, images, sounds,
/// fonts). Things in a scene can't be stored in a file, so each camera point is kept as its name, where it sat in
/// the hierarchy, and its position / rotation. Loading reconnects to objects with the same names, or creates
/// new points where they were.
/// Made and loaded from the Cinematic window (Save / Load button) or this file's Inspector.
/// </summary>
public class CinematicAsset : ScriptableObject
{
    [Serializable]
    public class Data
    {
        public bool playOnStart = true;
        public bool loop;
        public int displaySortOrder = -100;
        public Font font;
        public bool disableOtherCameras = true;
        public bool includeTextureCameras;
        public List<CinematicController.Shot> shots = new List<CinematicController.Shot>();
        public List<CinematicController.ImageLayer> images = new List<CinematicController.ImageLayer>();
        public List<CinematicController.SoundClip> sounds = new List<CinematicController.SoundClip>();
    }

    /// <summary>One link to a scene object (a camera point, look-at target, view, pop-up camera or UI object).</summary>
    [Serializable]
    public class PointRecord
    {
        public string slot;          // where it goes, e.g. "shots/2/point", "shots/2/views/1", "images/3/target"
        public string name;          // the object's name
        public string path;          // its place in the hierarchy, e.g. "Cinematic Points/Shot 3"
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public bool uiObject;        // a UI object (switched on by an overlay): can only be reconnected, not recreated
    }

    /// <summary>An image, sound or font baked into the file, so the cinematic works in a project that doesn't have it.</summary>
    [Serializable]
    public class BakedMedia
    {
        public string name;       // file name without extension
        public string extension;  // ".png", ".wav", ".ttf"...
        public string kind;       // "Texture", "Audio" or "Font"
        public string guid;       // where it came from (if that project has it, it's used directly)
        [HideInInspector] public byte[] bytes; // the original file
    }

    /// <summary>Which field uses which baked file (e.g. "images[2].texture" uses media 0).</summary>
    [Serializable]
    public class MediaSlot
    {
        public string path;
        public int media;
    }

    [Tooltip("Bake the images, sounds and fonts the cinematic uses into this file, so it works in other projects too " +
             "(they're unpacked there when it's loaded). Makes the file bigger.")]
    public bool bakeMedia = true;
    public List<BakedMedia> media = new List<BakedMedia>();
    public List<MediaSlot> mediaSlots = new List<MediaSlot>();

    [Tooltip("The scene it was saved from.")]
    public string sourceScene;
    public string savedAt;
    [TextArea(2, 6)] public string notes;
    public Data data = new Data();
    public List<PointRecord> points = new List<PointRecord>();
}
