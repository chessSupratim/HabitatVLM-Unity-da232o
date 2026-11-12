// RobotSkills.cs - the robot's pre-written skills (SPEC 6). The VLM decides WHAT, these decide HOW.
// Attach to "robot" (needs a NavMeshAgent and a child "gripper").
//
// Every skill is a coroutine that ends with a SkillResult { success, reason }.
// Use Execute(action, target, object, location, callback) to run any skill by name
// (that is what KeyboardDebug and, in Phase 6, AgentLoop do).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace HabitatVLM
{
    [Serializable]
    public class SkillResult
    {
        public bool success;
        public string reason;
        public static SkillResult Ok(string why = "") => new SkillResult { success = true, reason = why };
        public static SkillResult Fail(string why) => new SkillResult { success = false, reason = why };
        public override string ToString() => success ? "OK" + (string.IsNullOrEmpty(reason) ? "" : " - " + reason) : "FAILED - " + reason;
    }

    [RequireComponent(typeof(NavMeshAgent))]
    public class RobotSkills : MonoBehaviour
    {
        [Header("References (found automatically if empty)")]
        public Transform gripper;

        [Header("Tuning")]
        [Tooltip("Max horizontal distance from the gripper to an object for pick/place/open/close (SPEC 5.5).")]
        public float reach = 1.0f;
        [Tooltip("Objects within this horizontal distance of the robot are reported as 'near'.")]
        public float nearDistance = 1.3f;
        public float moveSpeed = 1.5f;
        public float turnSeconds = 0.5f;
        public float handSeconds = 0.4f;
        public bool logToConsole = true;

        public Interactable Holding { get; private set; }
        public bool Busy { get; private set; }
        public string LastSummary { get; private set; } = "";

        NavMeshAgent agent;
        float heldHalfHeight;

        public static readonly string[] Actions = { "move_to", "pick", "place", "open", "close", "look_around", "done" };

        public const string HomeName = "home_base";
        public static readonly Color HomeColor = new Color(0.55f, 0.57f, 0.62f);

        void Start()
        {
            // the robot's starting spot becomes a named location, so "return to base" is possible
            Vector3 p = transform.position;
            if (NavMesh.SamplePosition(p, out var hit, 1.0f, NavMesh.AllAreas)) p = hit.position;
            Interactable.CreateOrMoveLocation(HomeName, p, HomeColor);
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.speed = moveSpeed;
            agent.stoppingDistance = 0.05f;   // we compute the exact stand-point ourselves
            agent.autoBraking = true;
            if (gripper == null) gripper = transform.Find("gripper");
        }

        // ================================================================= dispatcher
        /// Run one command by name. `done` is always called exactly once.
        public IEnumerator Execute(string action, string target, string obj, string location, Action<SkillResult> done)
        {
            SkillResult result = null;
            void Set(SkillResult r) => result = r;

            if (Busy) { done?.Invoke(SkillResult.Fail("robot is busy with another action")); yield break; }
            Busy = true;
            StopWalking();   // a skill always takes over from a free click-to-move walk
            action = (action ?? "").Trim().ToLowerInvariant();

            switch (action)
            {
                case "move_to":     yield return MoveTo(target, Set); break;
                case "pick":        yield return Pick(obj, Set); break;
                case "place":       yield return Place(obj, location, Set); break;
                case "open":        yield return OpenClose(target, true, Set); break;
                case "close":       yield return OpenClose(target, false, Set); break;
                case "look_around": yield return LookAround(Set); break;
                case "done":        result = SkillResult.Ok("episode finished"); break;
                default:            result = SkillResult.Fail($"unknown action '{action}'"); break;
            }

            Busy = false;
            result ??= SkillResult.Fail("skill ended without a result");
            string args = string.Join(" ", new[] { target, obj, location }).Trim();
            LastSummary = $"{action}({args}) -> {result}";
            if (logToConsole)
            {
                if (result.success) Debug.Log("[Robot] " + LastSummary);
                else Debug.LogWarning("[Robot] " + LastSummary);
            }
            done?.Invoke(result);
        }

        // ================================================================= skills
        public IEnumerator MoveTo(string targetName, Action<SkillResult> done)
        {
            var target = Interactable.Find(targetName);
            if (target == null) { done(SkillResult.Fail($"no object named '{targetName}'")); yield break; }
            if (target == Holding) { done(SkillResult.Fail($"already holding {target.Name}")); yield break; }

            var b = target.Bounds;
            EnsureOnNavMesh();
            var path = new NavMeshPath();
            if (!FindStandPoint(target, out var hit, path))
            {
                Vector3 me = transform.position;
                done(SkillResult.Fail($"no path to {target.Name}" + (hit.hit ? "" : " (no floor nearby)")));
                Debug.LogWarning($"[Robot] no path: robot at ({me.x:F2}, {me.z:F2}), onNavMesh={agent.isOnNavMesh}, target {target.Name} at ({b.center.x:F2}, {b.center.z:F2})");
                yield break;
            }

            agent.isStopped = false;
            agent.SetPath(path);
            float length = 0;
            for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            float timeout = length / Mathf.Max(0.1f, agent.speed) + 5f;

            float t0 = Time.time;
            Vector3 dest = hit.position;
            yield return null;
            while (FlatDistance(transform.position, dest) > 0.15f)
            {
                if (Time.time - t0 > timeout)
                {
                    agent.ResetPath();
                    done(SkillResult.Fail($"timed out driving to {target.Name}"));
                    yield break;
                }
                yield return null;
            }
            agent.ResetPath();

            yield return FaceTowards(b.center);
            done(SkillResult.Ok($"near {target.Name}"));
        }

        public IEnumerator Pick(string itemName, Action<SkillResult> done)
        {
            var item = Interactable.Find(itemName);
            if (item == null) { done(SkillResult.Fail($"no object named '{itemName}'")); yield break; }
            if (item.kind != InteractableKind.Item) { done(SkillResult.Fail($"{item.Name} is a {item.KindName}, not an item")); yield break; }
            if (Holding == item) { done(SkillResult.Fail($"already holding {item.Name}")); yield break; }
            if (Holding != null) { done(SkillResult.Fail($"gripper is full (holding {Holding.Name})")); yield break; }
            if (item.location != null && item.location.kind == InteractableKind.Container && !item.location.IsOpen)
            { done(SkillResult.Fail($"{item.Name} is inside {item.location.Name}, which is closed")); yield break; }
            float d = item.HorizontalDistance(gripper.position);
            if (d > reach) { done(SkillResult.Fail($"{item.Name} is out of reach ({d:F1} m > {reach:F1} m); move_to it first")); yield break; }

            yield return FaceTowards(item.Bounds.center);
            heldHalfHeight = item.Bounds.extents.y;
            if (item.location != null) item.location.contents.Remove(item);
            item.location = null;
            item.isHeld = true;
            item.SetCollidersEnabled(false);

            yield return Glide(item.transform, gripper.position, handSeconds);
            item.transform.SetParent(gripper, true);
            item.transform.localPosition = Vector3.zero;
            Holding = item;
            done(SkillResult.Ok($"holding {item.Name}"));
        }

        public IEnumerator Place(string itemName, string locationName, Action<SkillResult> done)
        {
            var item = Interactable.Find(itemName);
            var loc = Interactable.Find(locationName);
            if (item == null) { done(SkillResult.Fail($"no object named '{itemName}'")); yield break; }
            if (loc == null) { done(SkillResult.Fail($"no object named '{locationName}'")); yield break; }
            if (Holding != item) { done(SkillResult.Fail(Holding == null ? "gripper is empty" : $"holding {Holding.Name}, not {item.Name}")); yield break; }
            if (loc.kind == InteractableKind.Item) { done(SkillResult.Fail($"{loc.Name} is an item; place on a surface, in a container or at a location")); yield break; }
            if (!loc.IsOpen) { done(SkillResult.Fail($"{loc.Name} is closed; open it first")); yield break; }
            float d = loc.HorizontalDistance(gripper.position);
            if (d > reach) { done(SkillResult.Fail($"{loc.Name} is out of reach ({d:F1} m > {reach:F1} m); move_to it first")); yield break; }

            Vector3 dest;
            if (loc.kind == InteractableKind.Location)
            {
                // on the floor right in front of the robot (it stands on / next to the spot)
                Vector3 g = gripper.position;
                dest = new Vector3(g.x, loc.transform.position.y + heldHalfHeight + 0.005f, g.z);
            }
            else
            {
                yield return FaceTowards(loc.Bounds.center);
                dest = loc.NextPlacePosition(item, heldHalfHeight);
            }
            item.transform.SetParent(ItemsRoot(), true);
            yield return Glide(item.transform, dest, handSeconds);
            item.transform.rotation = Quaternion.identity;
            item.SetCollidersEnabled(true);
            item.isHeld = false;
            item.location = loc;
            loc.contents.Add(item);
            Holding = null;
            done(SkillResult.Ok($"{item.Name} {(loc.kind == InteractableKind.Container ? "in" : loc.kind == InteractableKind.Location ? "at" : "on")} {loc.Name}"));
        }

        public IEnumerator OpenClose(string targetName, bool open, Action<SkillResult> done)
        {
            string verb = open ? "open" : "close";
            var t = Interactable.Find(targetName);
            if (t == null) { done(SkillResult.Fail($"no object named '{targetName}'")); yield break; }
            if (t.kind != InteractableKind.Container) { done(SkillResult.Fail($"{t.Name} cannot be {(open ? "opened" : "closed")}")); yield break; }
            if (t.Cabinet == null) { done(SkillResult.Fail($"{t.Name} has no door or lid; it is always open")); yield break; }
            if (t.Cabinet.IsOpen == open) { done(SkillResult.Fail($"{t.Name} is already {(open ? "open" : "closed")}")); yield break; }
            float d = t.HorizontalDistance(gripper.position);
            if (d > reach) { done(SkillResult.Fail($"{t.Name} is out of reach ({d:F1} m > {reach:F1} m); move_to it first")); yield break; }

            yield return FaceTowards(t.Bounds.center);
            yield return open ? t.Cabinet.Open() : t.Cabinet.Close();
            done(SkillResult.Ok($"{t.Name} is {(open ? "open" : "closed")}"));
        }

        // ================================================================= free walking (click-to-move, not a VLM action)
        Vector3? walkTarget;
        /// True while walking to a point chosen with the mouse.
        public bool Walking => walkTarget.HasValue;
        public Vector3? WalkTarget => walkTarget;

        /// Start walking to a floor point (click-to-move). Returns at once; a new call re-targets.
        public SkillResult GoToPoint(Vector3 point)
        {
            if (Busy) return SkillResult.Fail("robot is busy with another action");
            if (!NavMesh.SamplePosition(point, out var hit, 1.0f, NavMesh.AllAreas))
                return SkillResult.Fail("no walkable floor there");
            var path = new NavMeshPath();
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                return SkillResult.Fail("no path to that point");
            agent.isStopped = false;
            agent.SetPath(path);
            walkTarget = hit.position;
            return SkillResult.Ok($"walking to ({hit.position.x:F1}, {hit.position.z:F1})");
        }

        public void StopWalking()
        {
            if (!walkTarget.HasValue) return;
            walkTarget = null;
            if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        }

        void Update()
        {
            if (walkTarget.HasValue && !agent.pathPending && FlatDistance(transform.position, walkTarget.Value) <= 0.15f)
            {
                walkTarget = null;
                agent.ResetPath();
            }
        }

        public IEnumerator LookAround(Action<SkillResult> done)
        {
            Quaternion from = transform.rotation, to = from * Quaternion.Euler(0, 90, 0);
            for (float t = 0; t < 1f; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            transform.rotation = to;
            done(SkillResult.Ok("turned 90 degrees"));
        }

        /// Put the robot back at a pose with an empty gripper (room reset). Call only when idle.
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            walkTarget = null;
            agent.ResetPath();
            agent.Warp(position);
            transform.rotation = rotation;
            Holding = null;
            Busy = false;
            LastSummary = "";
        }

        // ================================================================= state for the bridge
        public RobotStateDto GetState()
        {
            var near = new List<string>();
            var containers = new List<NameState>();
            var items = new List<NameState>();
            foreach (var it in Interactable.All.Values)
            {
                if (it == null) continue;
                if (it.kind == InteractableKind.Item)
                {
                    string where = it.isHeld ? "held by robot"
                        : it.location == null ? "unknown"
                        : (it.location.kind == InteractableKind.Container ? "in "
                           : it.location.kind == InteractableKind.Location ? "on the floor at " : "on ") + it.location.Name;
                    items.Add(new NameState { name = it.Name, state = where });
                }
                if (it.isHeld) continue;
                if (it.HorizontalDistance(transform.position) <= nearDistance) near.Add(it.Name);
                if (it.kind == InteractableKind.Container)
                    containers.Add(new NameState { name = it.Name,
                        state = !it.HasDoor ? "open (no door/lid)" : it.IsOpen ? "open" : "closed" });
            }
            near.Sort();
            containers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return new RobotStateDto
            {
                holding = Holding != null ? Holding.Name : "",
                near = near.ToArray(),
                containers = containers.ToArray(),
                items = items.OrderBy(i => i.name).ToArray(),
            };
        }

        // ================================================================= helpers
        /// Where to stand for an object. Furniture: the middle of its longest side that has floor
        /// in front of it (shelf/cabinet front, open side of the table). Items: in front of the
        /// item, on that same side of the furniture it sits on.
        /// If the robot ended up just off the NavMesh (e.g. squeezed against a wall), snap it back on.
        void EnsureOnNavMesh()
        {
            if (agent.isOnNavMesh) return;
            if (NavMesh.SamplePosition(transform.position, out var h, 1.5f, NavMesh.AllAreas)) agent.Warp(h.position);
        }

        /// True if a complete path from the robot to p exists (fills path).
        bool Reachable(Vector3 p, NavMeshPath path) =>
            agent.isOnNavMesh && agent.CalculatePath(p, path) && path.status == NavMeshPathStatus.PathComplete;

        /// Like below, but only accepts stand points the robot can actually walk to (a side of an
        /// object can have floor that is a separate, unreachable NavMesh island).
        bool FindStandPoint(Interactable target, out NavMeshHit hit, NavMeshPath path)
        {
            NavMeshHit first = default;
            foreach (var c in StandCandidates(target))
            {
                if (!first.hit) first = c;
                if (Reachable(c.position, path)) { hit = c; return true; }
            }
            hit = first;   // hit.hit == false: no floor near the object at all
            return false;
        }

        IEnumerable<NavMeshHit> StandCandidates(Interactable target)
        {
            NavMeshHit hit;
            if (target.kind == InteractableKind.Location)
            {
                if (NavMesh.SamplePosition(target.transform.position, out hit, 0.6f, NavMesh.AllAreas)) yield return hit;
                if (NavMesh.SamplePosition(target.transform.position, out hit, 1.5f, NavMesh.AllAreas)) yield return hit;
                yield break;
            }

            var basis = target.kind == InteractableKind.Item && target.location != null
                        && target.location.kind != InteractableKind.Location ? target.location : target;
            var b = basis.Bounds;
            var itemCenter = target.Bounds.center;
            float standOff = 0.6f;    // baked NavMesh keeps 0.5 m from obstacles

            var faces = new List<(Vector3 normal, float length)>
            {
                (Vector3.forward, b.size.x), (Vector3.back, b.size.x),
                (Vector3.right,   b.size.z), (Vector3.left, b.size.z),
            };
            Vector3 me = transform.position;
            faces.Sort((f1, f2) =>
            {
                int byLength = f2.length.CompareTo(f1.length);           // long sides first
                if (Mathf.Abs(f1.length - f2.length) > 0.05f) return byLength;
                return FlatDistance(me, b.center + f1.normal).CompareTo(FlatDistance(me, b.center + f2.normal));
            });

            // A cabinet is used from its door side (built facing its local -Z), whatever wall it stands on
            if (basis.kind == InteractableKind.Container && basis.Cabinet != null && !basis.Cabinet.IsLid)
            {
                Vector3 f = -basis.transform.forward;
                Vector3 door = Mathf.Abs(f.x) > Mathf.Abs(f.z) ? new Vector3(Mathf.Sign(f.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(f.z));
                int i = faces.FindIndex(fc => fc.normal == door);
                if (i > 0) { var fc = faces[i]; faces.RemoveAt(i); faces.Insert(0, fc); }
            }

            foreach (var (n, _) in faces)
            {
                Vector3 p = b.center + Vector3.Scale(n, b.extents) + n * standOff;
                if (target != basis)  // slide along the side to be in front of the item
                {
                    if (n.x == 0) p.x = Mathf.Clamp(itemCenter.x, b.min.x, b.max.x);
                    else          p.z = Mathf.Clamp(itemCenter.z, b.min.z, b.max.z);
                }
                p.y = 0;
                if (NavMesh.SamplePosition(p, out hit, 0.4f, NavMesh.AllAreas)) yield return hit;
            }

            // fallback: nearest floor to the object's closest edge
            var edge = target.Bounds.ClosestPoint(new Vector3(me.x, b.center.y, me.z));
            edge.y = 0;
            if (NavMesh.SamplePosition(edge, out hit, 2.0f, NavMesh.AllAreas)) yield return hit;
        }

        IEnumerator FaceTowards(Vector3 worldPoint)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude < 1e-4f) yield break;
            Quaternion from = transform.rotation, to = Quaternion.LookRotation(dir);
            if (Quaternion.Angle(from, to) < 2f) yield break;
            for (float t = 0; t < turnSeconds; t += Time.deltaTime)
            {
                transform.rotation = Quaternion.Slerp(from, to, t / turnSeconds);
                yield return null;
            }
            transform.rotation = to;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0; b.y = 0;
            return Vector3.Distance(a, b);
        }

        static IEnumerator Glide(Transform tr, Vector3 to, float seconds)
        {
            Vector3 from = tr.position;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                tr.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds));
                yield return null;
            }
            tr.position = to;
        }

        Transform itemsRoot;
        Transform ItemsRoot()
        {
            if (itemsRoot == null)
            {
                var go = GameObject.Find("HabitatModule/Items");
                itemsRoot = go != null ? go.transform : null;
            }
            return itemsRoot;
        }
    }
}
