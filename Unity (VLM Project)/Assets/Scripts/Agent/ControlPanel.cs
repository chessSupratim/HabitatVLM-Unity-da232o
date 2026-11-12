// ControlPanel.cs - the on-screen UI: a sidebar on the right with four control modes.
// Added to "AgentManager" automatically (by AgentLoop or HabitatVLM > Setup).
//
// The main camera's viewport is shrunk so the sidebar sits NEXT TO the room, never over it.
// Hide it with the  >  button (or Tab) to get the full view back.
//
//   Tasks      task presets + success checks + suite runs (evaluation). Keyboard debug keys work here only.
//   Text       type a command ("pick up the blue canister and put it on the table"), Enter -> the VLM does it
//   Click      click the floor -> the robot walks there; click an object -> it walks up to it (no VLM)
//   Point+Say  click objects ("this", then "there"), type "put this there" -> the VLM does it
//
// In each mode only that kind of input is accepted (SPEC 5.7).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HabitatVLM
{
    public enum ControlMode { Tasks, Text, Click, Point }

    [RequireComponent(typeof(AgentLoop))]
    public class ControlPanel : MonoBehaviour
    {
        public static ControlMode Mode { get; private set; } = ControlMode.Tasks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Mode = ControlMode.Tasks;

        [Header("Layout")]
        public float sidebarWidth = 400f;
        public bool visible = true;

        AgentLoop agent;
        ClickControl clicks;
        Camera mainCam;

        // Text / Point+Say command box
        string command = "";
        readonly List<string> history = new List<string>();
        int historyIndex = -1;
        bool focusCommand;

        Vector2 scroll;
        int seenLogVersion = -1;

        static readonly string[] ModeNames = { "Tasks", "Text", "Click", "Point+Say" };
        static readonly string[] ModeHelp =
        {
            "Run a preset task (success is checked) or the whole suite.  Debug keys 1-0 / R work in this mode (H lists them).",
            "Type a command and press Enter, e.g. \"pick up the blue canister and put it on the table\" or " +
                "\"bring the red canister back to base\" (the grey disc = home_base). The robot continues from where things are now.",
            "Click the floor: the robot walks there.  Click an object: it walks up to it.  (No VLM.)",
            "1) Click an object or the floor for \"this\".  2) Optionally click a second one for \"there\".  " +
                "3) Type and press Enter, e.g. \"put this there\", \"bring this to home_base\", \"go there\".  " +
                "Floor clicks place the orange marked_spot.",
        };

        /// Screen-pixel width the sidebar takes (0 when hidden).
        public float Width => visible ? Mathf.Min(sidebarWidth, Screen.width * 0.45f) : 0f;

        void Awake()
        {
            agent = GetComponent<AgentLoop>();
            clicks = GetComponent<ClickControl>();
        }

        void Update()
        {
            if (clicks == null) clicks = GetComponent<ClickControl>();
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame && GUIUtility.keyboardControl == 0) visible = !visible;

            // keep the room view beside the sidebar instead of under it
            if (mainCam == null) mainCam = Camera.main;
            if (mainCam != null)
            {
                float w = Screen.width > 0 ? Width / Screen.width : 0f;
                var r = new Rect(0, 0, 1f - w, 1f);
                if (mainCam.rect != r) mainCam.rect = r;
            }
        }

        void OnDisable()
        {
            if (mainCam != null) mainCam.rect = new Rect(0, 0, 1, 1);
        }

        public void SetMode(ControlMode m)
        {
            if (m == Mode || agent.Busy) return;
            Mode = m;
            command = "";
            if (agent.Robot != null) agent.Robot.StopWalking();
            if (clicks != null) clicks.ClearPointing();
            agent.Status = ModeNames[(int)m] + " mode.";
            GUIUtility.keyboardControl = 0;
            focusCommand = m == ControlMode.Text || m == ControlMode.Point;
        }

        // ============================================================ drawing
        void OnGUI()
        {
            if (agent == null) return;
            float w = Width;
            if (!visible)
            {
                if (GUI.Button(new Rect(Screen.width - 34, 10, 26, 60), "<")) visible = true;
                return;
            }

            var area = new Rect(Screen.width - w, 0, w, Screen.height);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 8, 8, w - 16, Screen.height - 16));

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>HabitatVLM</b>  <size=11>{agent.BackendInfo}</size>", Rich());
            if (GUILayout.Button(">", GUILayout.Width(26))) visible = false;
            GUILayout.EndHorizontal();

            // robot camera preview
            var tex = agent.RobotCamera != null ? agent.RobotCamera.Texture : null;
            if (tex != null)
            {
                float side = Mathf.Min(w - 16, 200);
                var r = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(false));
                r.x += (w - 16 - side) / 2;
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            }

            // mode buttons (locked while the VLM is working)
            GUI.enabled = !agent.Busy;
            int picked = GUILayout.Toolbar((int)Mode, ModeNames, GUILayout.Height(26));
            if (picked != (int)Mode) SetMode((ControlMode)picked);
            GUI.enabled = true;
            GUILayout.Label("<size=11>" + ModeHelp[(int)Mode] + "</size>", Rich());

            switch (Mode)
            {
                case ControlMode.Tasks: DrawTasks(); break;
                case ControlMode.Text:  DrawCommand(false); break;
                case ControlMode.Click: DrawClick(); break;
                case ControlMode.Point: DrawCommand(true); break;
            }

            // status, robot state, log
            GUILayout.Space(4);
            GUILayout.Label("<b>Status:</b> " + agent.Status, Rich());
            if (!string.IsNullOrEmpty(agent.LastThought) && agent.Busy)
                GUILayout.Label("<b>Thought:</b> " + agent.LastThought, Rich());
            if (agent.Robot != null)
            {
                var st = agent.Robot.GetState();
                string holding = string.IsNullOrEmpty(st.holding) ? "nothing" : st.holding;
                string near = st.near.Length == 0 ? "nothing" : string.Join(", ", st.near);
                GUILayout.Label($"<size=11><b>Holding:</b> {holding}    <b>Near:</b> {near}</size>", Rich());
            }

            if (agent.LogVersion != seenLogVersion) { seenLogVersion = agent.LogVersion; scroll.y = float.MaxValue; }
            scroll = GUILayout.BeginScrollView(scroll, GUI.skin.box);
            foreach (var line in agent.Log) GUILayout.Label("<size=11>" + Escape(line) + "</size>", Rich());
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // in-scene labels for pointed objects / click target
            if (clicks != null) clicks.DrawSceneLabels();
        }

        void DrawTasks()
        {
            bool idle = !agent.Busy;
            GUI.enabled = idle;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(28))) agent.SelectPreset(-1);
            GUILayout.Label(agent.PresetLabel, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(28))) agent.SelectPreset(+1);
            GUILayout.EndHorizontal();
            agent.TaskText = GUILayout.TextField(agent.TaskText);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start")) agent.StartEpisode();
            GUI.enabled = agent.Busy;
            if (GUILayout.Button("Stop")) agent.RequestStop();
            GUI.enabled = idle;
            if (GUILayout.Button("Reset room")) agent.ResetRoom();
            GUILayout.EndHorizontal();

            var runner = agent.Runner;
            if (runner != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Suite:", GUILayout.Width(40));
                foreach (var t in new[] { "all", "easy", "medium", "hard", "compare" })
                    if (GUILayout.Toggle(runner.tierFilter == t, t, GUILayout.Width(t == "compare" ? 66 : 54))) runner.tierFilter = t;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("repeats x" + runner.repeats, GUILayout.Width(80));
                if (GUILayout.Button("-", GUILayout.Width(22))) runner.repeats = Mathf.Max(1, runner.repeats - 1);
                if (GUILayout.Button("+", GUILayout.Width(22))) runner.repeats = Mathf.Min(10, runner.repeats + 1);
                if (GUILayout.Button("Run all")) runner.StartSuite();
                GUILayout.EndHorizontal();
                GUI.enabled = true;
                if (!string.IsNullOrEmpty(runner.Progress)) GUILayout.Label("<b>Suite:</b> " + runner.Progress, Rich());
            }
            GUI.enabled = true;
        }

        void DrawCommand(bool pointing)
        {
            if (pointing && clicks != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>this</b> = {clicks.PointedThis ?? "(click an object)"}    " +
                                $"<b>there</b> = {clicks.PointedThere ?? "-"}", Rich());
                if (GUILayout.Button("Clear", GUILayout.Width(52))) clicks.ClearPointing();
                GUILayout.EndHorizontal();
            }

            // Enter sends, Up/Down walk through the history (handled before the text field eats the key)
            var e = Event.current;
            bool send = false;
            if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == "command")
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { send = true; e.Use(); }
                else if (e.keyCode == KeyCode.UpArrow && history.Count > 0)
                {
                    historyIndex = historyIndex < 0 ? history.Count - 1 : Mathf.Max(0, historyIndex - 1);
                    command = history[historyIndex]; e.Use();
                }
                else if (e.keyCode == KeyCode.DownArrow && historyIndex >= 0)
                {
                    historyIndex++;
                    command = historyIndex < history.Count ? history[historyIndex] : "";
                    if (historyIndex >= history.Count) historyIndex = -1;
                    e.Use();
                }
            }

            GUI.enabled = !agent.Busy;
            GUI.SetNextControlName("command");
            command = GUILayout.TextField(command, GUILayout.Height(24));
            if (focusCommand && e.type == EventType.Repaint) { GUI.FocusControl("command"); focusCommand = false; }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Send  (Enter)")) send = true;
            GUI.enabled = agent.Busy;
            if (GUILayout.Button("Stop")) agent.RequestStop();
            GUI.enabled = !agent.Busy;
            if (GUILayout.Button("Reset room")) agent.ResetRoom();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (pointing && clicks != null)
            {
                string preview = clicks.Preview(command);
                if (preview != null) GUILayout.Label("<size=11><b>Will send:</b> " + Escape(preview) + "</size>", Rich());
            }

            if (send && !agent.Busy) Send(pointing);
        }

        void Send(bool pointing)
        {
            string text = command.Trim();
            if (text.Length == 0) return;
            string task = text;
            if (pointing)
            {
                if (clicks == null || clicks.PointedThis == null)
                {
                    agent.Status = "Click an object or the floor first (it becomes \"this\").";
                    return;
                }
                task = clicks.ComposeTask(text);
            }
            if (!agent.StartCommand(task, pointing ? "POINT" : "TEXT")) return;
            if (history.Count == 0 || history[history.Count - 1] != text) history.Add(text);
            historyIndex = -1;
            command = "";
            if (pointing) clicks.ClearPointing();
        }

        void DrawClick()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Stop walking") && agent.Robot != null) agent.Robot.StopWalking();
            GUI.enabled = !agent.Busy;
            if (GUILayout.Button("Reset room")) agent.ResetRoom();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        GUIStyle rich;
        GUIStyle Rich()
        {
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            return rich;
        }

        // log lines can contain '<' (e.g. "a <-> b"); keep rich text from eating them
        static string Escape(string s) => s.Replace("<", "‹").Replace(">", "›");
    }
}
