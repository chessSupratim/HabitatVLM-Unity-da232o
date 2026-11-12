// ClickControl.cs - mouse input for the Click and Point+Say modes (see ControlPanel).
// Added to "AgentManager" automatically.
//
//   Click      floor  -> robot walks to that point (RobotSkills.GoToPoint, no VLM)
//              object -> robot walks up to it (the normal move_to skill)
//   Point+Say  1st click = "this", 2nd = "there" (a 3rd click starts over). A click on an object
//              points at it; a click on the floor puts the "marked_spot" disc there and points at it.
//              ComposeTask() adds the pointed names to the typed command for the VLM,
//              e.g. "put this there (\"this\" = blue_canister, \"there\" = table)".
//
// Clicks are read only in those two modes, only inside the room view (not on the sidebar),
// and never while the VLM is running an episode.
using UnityEngine;
using UnityEngine.InputSystem;

namespace HabitatVLM
{
    [RequireComponent(typeof(AgentLoop))]
    public class ClickControl : MonoBehaviour
    {
        public Color markerColor = new Color(0.10f, 0.75f, 0.95f);
        public Color spotColor = new Color(0.95f, 0.55f, 0.10f);
        public const string SpotName = "marked_spot";

        AgentLoop agent;
        Camera cam;
        GameObject marker;

        Interactable pointedThis, pointedThere;
        public string PointedThis => pointedThis != null ? pointedThis.Name : null;
        public string PointedThere => pointedThere != null ? pointedThere.Name : null;

        void Awake() => agent = GetComponent<AgentLoop>();

        void Update()
        {
            var robot = agent.Robot;
            if (marker != null) marker.SetActive(robot != null && robot.Walking && ControlPanel.Mode == ControlMode.Click);

            var mode = ControlPanel.Mode;
            if (mode != ControlMode.Click && mode != ControlMode.Point) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (agent.Busy || robot == null) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            Vector2 pos = mouse.position.ReadValue();
            if (!cam.pixelRect.Contains(pos)) return;          // click was on the sidebar
            if (!RaycastVisible(cam, cam.ScreenPointToRay(pos), robot.transform, out var hit)) return;

            var it = hit.collider.GetComponentInParent<Interactable>();
            if (it != null && it.isHeld) it = null;            // the item in the gripper is not a target

            if (mode == ControlMode.Click) HandleMoveClick(robot, it, hit.point);
            else HandlePointClick(it, hit.point);
        }

        /// First thing the user can actually SEE under the mouse. Skips colliders whose renderer is
        /// switched off or whose layer this camera does not draw (the front wall / ceiling that only
        /// the robot camera sees; otherwise they block clicks on everything behind them), and the robot.
        static bool RaycastVisible(Camera cam, Ray ray, Transform robot, out RaycastHit hit)
        {
            var hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (robot != null && h.collider.transform.IsChildOf(robot)) continue;
                var r = h.collider.GetComponent<Renderer>();
                if (r != null && !r.enabled) continue;
                if ((cam.cullingMask & (1 << h.collider.gameObject.layer)) == 0) continue;
                hit = h;
                return true;
            }
            hit = default;
            return false;
        }

        // ------------------------------------------------------------ Click mode
        void HandleMoveClick(RobotSkills robot, Interactable it, Vector3 point)
        {
            if (robot.Busy) { agent.Status = "Robot is busy; wait for it to finish."; return; }
            if (it != null)
            {
                agent.AddLog($"[click] move_to({it.Name})");
                agent.Status = $"Walking to {it.Name}...";
                StartCoroutine(robot.Execute("move_to", it.Name, null, null, r =>
                {
                    agent.AddLog("    -> " + r);
                    agent.Status = r.success ? $"Arrived: {r.reason}" : "Could not get there: " + r.reason;
                }));
                return;
            }
            var res = robot.GoToPoint(point);
            agent.Status = res.success ? res.reason : "Can't walk there: " + res.reason;
            if (res.success)
            {
                agent.AddLog($"[click] {res.reason}");
                ShowMarker(robot.WalkTarget.Value);
            }
        }

        void ShowMarker(Vector3 at)
        {
            if (marker == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "click_target_marker";
                Destroy(marker.GetComponent<Collider>());
                marker.transform.localScale = new Vector3(0.35f, 0.005f, 0.35f);
                var r = marker.GetComponent<Renderer>();
                r.material.color = markerColor;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            marker.transform.position = at + Vector3.up * 0.01f;
            marker.SetActive(true);
        }

        // ------------------------------------------------------------ Point+Say mode
        void HandlePointClick(Interactable it, Vector3 point)
        {
            if (it == null)
            {
                // floor (or wall): mark a reachable spot on the floor
                if (!UnityEngine.AI.NavMesh.SamplePosition(point, out var nav, 1.0f, UnityEngine.AI.NavMesh.AllAreas))
                { agent.Status = "The robot can't stand there; click open floor."; return; }
                if (pointedThis != null && pointedThis.Name == SpotName && pointedThere == null)
                    pointedThis = null;   // moving the spot that is already "this": just re-point it
                it = Interactable.CreateOrMoveLocation(SpotName, nav.position, spotColor);
            }
            if (pointedThis == null || pointedThere != null)
            {
                pointedThis = it; pointedThere = null;
                agent.Status = $"\"this\" = {it.Name}. Click another object for \"there\", or type the command.";
            }
            else if (it != pointedThis)
            {
                pointedThere = it;
                agent.Status = $"\"there\" = {it.Name}. Now type the command, e.g. \"put this there\".";
            }
        }

        public void ClearPointing() { pointedThis = null; pointedThere = null; }

        static string Describe(Interactable it) =>
            it.kind == InteractableKind.Location ? $"{it.Name} (a spot on the floor)"
            : it.kind == InteractableKind.Surface ? $"{it.Name} (furniture: a surface, cannot be carried)"
            : it.kind == InteractableKind.Container ? $"{it.Name} (furniture: a container, cannot be carried)"
            : it.Name;

        /// What Send would give the VLM right now (shown in the panel).
        public string Preview(string text) =>
            pointedThis == null || string.IsNullOrWhiteSpace(text) ? null : ComposeTask(text.Trim());

        /// The command for the VLM: the user's words plus what "this"/"there" refer to.
        public string ComposeTask(string text)
        {
            string refs = $"\"this\" = {Describe(pointedThis)}";
            if (pointedThere != null) refs += $", \"there\" = {Describe(pointedThere)}";
            return $"{text}  (the user pointed at: {refs})";
        }

        // ------------------------------------------------------------ labels (called from ControlPanel.OnGUI)
        GUIStyle labelStyle;
        public void DrawSceneLabels()
        {
            if (ControlPanel.Mode != ControlMode.Point) return;
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            if (labelStyle == null)
                labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Label(pointedThis, "this");
            Label(pointedThere, "there");
        }

        void Label(Interactable it, string word)
        {
            if (it == null) return;
            var b = it.Bounds;
            Vector3 s = cam.WorldToScreenPoint(b.center + Vector3.up * (b.extents.y + 0.1f));
            if (s.z <= 0 || !cam.pixelRect.Contains(s)) return;
            var size = labelStyle.CalcSize(new GUIContent(word));
            var r = new Rect(s.x - size.x / 2 - 4, Screen.height - s.y - size.y - 4, size.x + 8, size.y + 4);
            GUI.Box(r, word, labelStyle);
        }

        void OnDestroy()
        {
            if (marker != null) Destroy(marker);
        }
    }
}
