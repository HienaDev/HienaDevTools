using System.Collections.Generic;
using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>Where a room exported by the Building Builder can join another: a doorway on its outside, facing out. A level
    /// generator lines up two connectors (facing each other) to put rooms together.</summary>
    [AddComponentMenu("Dev Tools/Room Connector")]
    public class RoomConnector : MonoBehaviour
    {
        public float width = 1, height = 2.2f; [Tooltip("Used to tell kinds apart: door, arch, corridor...")] public string kind = "door";
        void OnDrawGizmos() { Gizmos.color = new Color(1f, 0.62f, 0.2f); Gizmos.matrix = transform.localToWorldMatrix; Gizmos.DrawWireCube(new Vector3(0, height / 2, 0), new Vector3(width, height, 0.05f)); Gizmos.DrawLine(new Vector3(0, 0.05f, 0), new Vector3(0, 0.05f, 0.6f)); }
    }
}
