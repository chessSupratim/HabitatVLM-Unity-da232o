// Cabinet.cs - opens/closes a cabinet door by rotating its "hinge" child (SPEC 10).
// The door is a child of "hinge" (at the door's left edge); +100 deg on Y swings it out into the room.
// A lid (storage tub) uses axis = (1,0,0): the hinge sits on the back top edge and the lid swings up.
using System.Collections;
using UnityEngine;

namespace HabitatVLM
{
    public class Cabinet : MonoBehaviour
    {
        public Transform hinge;
        public float openAngle = 100f;
        public float duration = 1f;
        [Tooltip("Hinge axis in the hinge's local space: up = door, right = lid.")]
        public Vector3 axis = Vector3.up;

        public bool IsLid => Mathf.Abs(axis.y) < 0.5f;
        Quaternion Pose(bool open) => Quaternion.AngleAxis(open ? openAngle : 0f, axis);

        [SerializeField] bool isOpen;
        public bool IsOpen => isOpen;
        public bool IsMoving { get; private set; }

        void Awake()
        {
            if (hinge == null) hinge = transform.Find("hinge");
            if (hinge != null) hinge.localRotation = Pose(isOpen);
        }

        /// Jump straight to open/closed (used when resetting the room).
        public void SetOpenImmediate(bool open)
        {
            StopAllCoroutines();
            if (hinge != null) hinge.localRotation = Pose(open);
            isOpen = open;
            IsMoving = false;
        }

        public IEnumerator Open()  { yield return Swing(true); }
        public IEnumerator Close() { yield return Swing(false); }

        IEnumerator Swing(bool open)
        {
            if (hinge == null) yield break;
            IsMoving = true;
            Quaternion from = hinge.localRotation;
            Quaternion to = Pose(open);
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                hinge.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, t / duration));
                yield return null;
            }
            hinge.localRotation = to;
            isOpen = open;
            IsMoving = false;
        }
    }
}
