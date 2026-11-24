using UnityEngine;

namespace HienaDev.DevTools
{
    /// <summary>A door leaf made by the Building Builder. It swings open when something with a Rigidbody or a
    /// CharacterController comes near (or call Open, Close, Toggle), and closes again after a while.</summary>
    [AddComponentMenu("Dev Tools/Building Door")]
    public class BuildingDoor : MonoBehaviour
    {
        [Tooltip("Degrees it opens (its sign: which way)")] public float angle = 95; [Tooltip("Swings away from whoever comes, whichever side they're on")] public bool bothWays = true; [Tooltip("Opens by itself when something comes this near (0: only when told)")] public float near = 1.6f; public float speed = 3; [Tooltip("Seconds before it closes again (0: stays open)")] public float closeAfter = 3; public bool open;
        float m_Now, m_Since, m_Way = 1; Quaternion m_Shut; void Awake() { m_Shut = transform.localRotation; }
        public void Open() { open = true; m_Since = 0; } public void Close() { open = false; } public void Toggle() { if (open) Close(); else Open(); }
        void Update()
        {
            if (near > 0 && !open) foreach (var c in Physics.OverlapSphere(transform.position, near)) if (c.attachedRigidbody || c is CharacterController) { if (!c.transform.IsChildOf(transform.parent)) { if (bothWays) { var fw = (transform.parent ? transform.parent.rotation : Quaternion.identity) * m_Shut * Vector3.forward; m_Way = Vector3.Dot(c.transform.position - transform.position, fw) > 0 ? 1 : -1; } Open(); break; } }
            if (open && closeAfter > 0) { m_Since += Time.deltaTime; if (m_Since > closeAfter) Close(); }
            m_Now = Mathf.MoveTowards(m_Now, open ? 1 : 0, Time.deltaTime * speed); transform.localRotation = m_Shut * Quaternion.Euler(0, Mathf.SmoothStep(0, bothWays ? Mathf.Abs(angle) * m_Way * Mathf.Sign(angle) : angle, m_Now), 0);
        }
    }

}
