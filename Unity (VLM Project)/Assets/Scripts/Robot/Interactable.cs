// Interactable.cs - marks an object the VLM may name (SPEC 5.3).
// Added automatically by the menu  HabitatVLM > Setup Interactables & Robot.
//
//   kind       item (can be picked) | surface (things are placed on top) | container (cabinet)
//              | location (a named spot on the floor: home_base, marked_spot; created at runtime)
//   snapPoint  where placed items go (top of a surface, floor of a container)
//   location   for items: the surface/container the item currently sits on/in
using System.Collections.Generic;
using UnityEngine;

namespace HabitatVLM
{
    public enum InteractableKind { Item, Surface, Container, Location }   // keep order (serialized as int)

    public class Interactable : MonoBehaviour
    {
        public InteractableKind kind = InteractableKind.Item;
        public Transform snapPoint;

        [Tooltip("Items only: where the item is right now (filled automatically at start).")]
        public Interactable location;

        [HideInInspector] public bool isHeld;

        public readonly List<Interactable> contents = new List<Interactable>();

        public string Name => gameObject.name;
        public string KindName => kind.ToString().ToLowerInvariant();

        Cabinet cabinet;
        public Cabinet Cabinet => cabinet != null ? cabinet : (cabinet = GetComponent<Cabinet>());

        /// Containers: open state comes from the Cabinet (door or lid). A container without one
        /// (open-top basket) and surfaces are always "open".
        public bool IsOpen => kind != InteractableKind.Container || Cabinet == null || Cabinet.IsOpen;
        public bool HasDoor => kind == InteractableKind.Container && Cabinet != null;

        // ------------------------------------------------------------ registry (name -> object)
        static readonly Dictionary<string, Interactable> registry = new Dictionary<string, Interactable>();
        public static IReadOnlyDictionary<string, Interactable> All => registry;

        // Project has domain reload disabled, so clear statics at every Play
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => registry.Clear();

        public static Interactable Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            registry.TryGetValue(name.Trim().ToLowerInvariant(), out var it);
            return it;
        }

        void OnEnable()  { registry[Name.ToLowerInvariant()] = this; }
        void OnDisable() { if (Find(Name) == this) registry.Remove(Name.ToLowerInvariant()); }

        void Start()
        {
            if (kind == InteractableKind.Item && location == null)
                location = FindSupportBelow();
            if (location != null && !location.contents.Contains(this))
                location.contents.Add(this);
        }

        /// The surface/container directly under this item (raycast down).
        Interactable FindSupportBelow()
        {
            var hits = Physics.RaycastAll(Bounds.center, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var it = h.collider.GetComponentInParent<Interactable>();
                if (it != null && it != this && it.kind != InteractableKind.Item) return it;
            }
            return null;
        }

        // ------------------------------------------------------------ geometry helpers
        /// World-space bounds of this object's renderers (a cabinet's swinging door is left out,
        /// so an open door doesn't make the cabinet look bigger).
        public Bounds Bounds
        {
            get
            {
                Transform hinge = Cabinet != null ? Cabinet.hinge : null;
                bool any = false;
                var b = new Bounds(transform.position, Vector3.one * 0.1f);
                foreach (var r in GetComponentsInChildren<Renderer>())
                {
                    if (hinge != null && r.transform.IsChildOf(hinge)) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                return b;
            }
        }

        /// Horizontal (XZ) distance from a point to the closest point of this object.
        public float HorizontalDistance(Vector3 from)
        {
            var b = Bounds;
            var p = new Vector3(from.x, b.center.y, from.z);
            var c = b.ClosestPoint(p);
            c.y = p.y;
            return Vector3.Distance(p, c);
        }

        /// Where the next placed item goes: snap point plus a side offset per item already there.
        /// Where a placed item goes: the free spot along the snap point's row (left-right),
        /// nearest the middle, that does not overlap items already there.
        public Vector3 NextPlacePosition(Interactable item, float itemHalfHeight)
        {
            Transform sp = snapPoint != null ? snapPoint : transform;
            float itemRadius = HorizontalRadius(item);

            // usable half-sizes of the area around the snap point (inside walls for containers)
            var b = Bounds;
            float margin = itemRadius + (kind == InteractableKind.Container ? 0.07f : 0.02f);
            float halfLen = Mathf.Max(0f, HalfExtentAlong(b, sp.right) - margin);
            float halfDepth = Mathf.Max(0f, HalfExtentAlong(b, sp.forward) - margin);

            Vector3 best = sp.position;
            float bestClearance = float.NegativeInfinity;
            // rows: middle first, then front/back; within a row: middle outwards
            for (int r = 0; r <= 4; r++)
            {
                float row = (r % 2 == 1 ? -1 : 1) * ((r + 1) / 2) * 0.15f;     // 0, -0.15, +0.15, -0.3, +0.3
                if (Mathf.Abs(row) > halfDepth + 1e-4f) continue;
                for (int i = 0; i <= 12; i++)
                {
                    float offset = (i % 2 == 1 ? -1 : 1) * ((i + 1) / 2) * 0.08f;   // 0, -0.08, +0.08, ...
                    if (Mathf.Abs(offset) > halfLen + 1e-4f) continue;
                    Vector3 p = sp.position + sp.right * offset + sp.forward * row;
                    float clearance = float.PositiveInfinity;
                    foreach (var c in contents)
                    {
                        if (c == item || c == null) continue;
                        Vector3 cp = c.Bounds.center;
                        float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(cp.x, cp.z));
                        clearance = Mathf.Min(clearance, d - itemRadius - HorizontalRadius(c));
                    }
                    if (clearance >= 0.015f) return p + Vector3.up * (itemHalfHeight + 0.005f);  // first free spot
                    if (clearance > bestClearance) { best = p; bestClearance = clearance; }      // else least crowded
                }
            }
            return best + Vector3.up * (itemHalfHeight + 0.005f);
        }

        static float HalfExtentAlong(Bounds b, Vector3 dir)
        {
            return Mathf.Abs(b.extents.x * dir.x) + Mathf.Abs(b.extents.z * dir.z);
        }

        static float HorizontalRadius(Interactable it)
        {
            var e = it.Bounds.extents;
            return Mathf.Max(e.x, e.z);
        }

        /// Create (or move) a named floor spot shown as a flat disc. The disc has a collider so it
        /// can be clicked, and the robot camera can see it.
        public static Interactable CreateOrMoveLocation(string name, Vector3 floorPoint, Color color)
        {
            var it = Find(name);
            if (it == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = name;
                go.transform.localScale = new Vector3(0.6f, 0.004f, 0.6f);
                var r = go.GetComponent<Renderer>();
                r.material.color = color;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                it = go.AddComponent<Interactable>();     // OnEnable registers the name
                it.kind = InteractableKind.Location;
                it.snapPoint = go.transform;
            }
            it.transform.position = new Vector3(floorPoint.x, floorPoint.y + 0.005f, floorPoint.z);
            return it;
        }

        public void SetCollidersEnabled(bool on)
        {
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = on;
        }
    }
}
