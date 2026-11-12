// KeyboardDebug.cs - drive the robot's skills by keyboard (PLAN Phase 5). Dev/testing only.
// Attach to "robot" (next to RobotSkills). Click the Game view first so it receives keys.
//
//  1 move_to shelf_a        2 pick red_canister       3 move_to cabinet_left
//  4 open cabinet_left      5 place red_canister in cabinet_left      6 close cabinet_left
//  7 look_around            8 pick red_canister (again, e.g. after reopening)
//  9 move_to table          0 place red_canister on table
//  R reload the scene (reset)        H hide/show this help
// Keys work only in the panel's Tasks mode (other modes accept only their own input).
// The help box starts hidden (it covered the room view); H shows it at the bottom-left.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HabitatVLM
{
    [RequireComponent(typeof(RobotSkills))]
    public class KeyboardDebug : MonoBehaviour
    {
        public bool showHelp = true;

        RobotSkills robot;
        readonly List<string> log = new List<string>();

        struct Cmd { public Key key; public string label, action, target, obj, location; }

        static readonly Cmd[] Commands =
        {
            new Cmd { key = Key.Digit1, label = "move_to shelf_a",                   action = "move_to", target = "shelf_a" },
            new Cmd { key = Key.Digit2, label = "pick red_canister",                 action = "pick",    obj = "red_canister" },
            new Cmd { key = Key.Digit3, label = "move_to cabinet_left",              action = "move_to", target = "cabinet_left" },
            new Cmd { key = Key.Digit4, label = "open cabinet_left",                 action = "open",    target = "cabinet_left" },
            new Cmd { key = Key.Digit5, label = "place red_canister -> cabinet_left", action = "place",   obj = "red_canister", location = "cabinet_left" },
            new Cmd { key = Key.Digit6, label = "close cabinet_left",                action = "close",   target = "cabinet_left" },
            new Cmd { key = Key.Digit7, label = "look_around",                       action = "look_around" },
            new Cmd { key = Key.Digit8, label = "pick red_canister",                 action = "pick",    obj = "red_canister" },
            new Cmd { key = Key.Digit9, label = "move_to table",                     action = "move_to", target = "table" },
            new Cmd { key = Key.Digit0, label = "place red_canister -> table",       action = "place",   obj = "red_canister", location = "table" },
        };

        void Awake()
        {
            robot = GetComponent<RobotSkills>();
            showHelp = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (GUIUtility.keyboardControl != 0) return;   // user is typing in a text box (e.g. the task)
            if (ControlPanel.Mode != ControlMode.Tasks) return;
            if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
            if (kb.rKey.wasPressedThisFrame) { ReloadScene(); return; }
            if (robot.Busy) return;

            foreach (var c in Commands)
            {
                if (!kb[c.key].wasPressedThisFrame) continue;
                AddLog($"> {c.label}");
                StartCoroutine(robot.Execute(c.action, c.target, c.obj, c.location, r => AddLog($"   {r}")));
                break;
            }
        }

        /// Restart the scene. In the editor this works even if the scene is not in Build Settings.
        public static void ReloadScene()
        {
            var scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.buildIndex);
#endif
        }

        void AddLog(string line)
        {
            log.Add(line);
            if (log.Count > 8) log.RemoveAt(0);
        }

        void OnGUI()
        {
            if (!showHelp || ControlPanel.Mode != ControlMode.Tasks) return;
            const float w = 380, h = 330;
            var area = new Rect(10, Screen.height - h - 10, w, h);   // bottom-left of the room view
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = true };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("KEYBOARD DEBUG  (H hide, R reset)");
            foreach (var c in Commands) sb.AppendLine($"{c.key.ToString().Replace("Digit", "")}  {c.label}");
            var st = robot.GetState();
            sb.AppendLine();
            sb.AppendLine($"holding: {(string.IsNullOrEmpty(st.holding) ? "nothing" : st.holding)}   {(robot.Busy ? "[BUSY]" : "")}");
            sb.AppendLine($"near: {(st.near.Length == 0 ? "nothing" : string.Join(", ", st.near))}");
            foreach (var l in log) sb.AppendLine(l);
            GUI.Box(area, sb.ToString(), style);
        }
    }
}
