// HabitatSetup.cs - one-click wiring of Phase 5 components into the Habitat scene.
// Menu:  HabitatVLM > Setup Interactables & Robot
//
// Works for ANY room (SPEC 5.6): it finds every object that already has an Interactable
// (RoomBuilder adds them from the layout) and completes it (snap point, Cabinet + hinge).
// Room 01 objects without an Interactable are still recognised by the legacy name table.
// Adds RobotSkills + KeyboardDebug to the robot, CameraCapture to robot_cam, and the
// AgentManager (BridgeClient, AgentLoop, SceneReset, TaskRunner, ControlPanel, ClickControl).
// Safe to run more than once. Also called by Build Habitat Room and RoomBuilder.
#if UNITY_EDITOR
using System.Collections.Generic;
using HabitatVLM;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class HabitatSetup
{
    // Legacy: room 01 object names (used only for objects that have no Interactable yet)
    static readonly Dictionary<string, InteractableKind> Kinds = new Dictionary<string, InteractableKind>
    {
        ["red_canister"]   = InteractableKind.Item,
        ["blue_canister"]  = InteractableKind.Item,
        ["green_canister"] = InteractableKind.Item,
        ["toolbox"]        = InteractableKind.Item,
        ["shelf_a"]        = InteractableKind.Surface,
        ["table"]          = InteractableKind.Surface,
        ["cabinet_left"]   = InteractableKind.Container,
        ["cabinet_right"]  = InteractableKind.Container,
    };

    [MenuItem("HabitatVLM/Setup Interactables and Robot")]
    public static void Run() => Run(null);

    /// tasksPath: the room's task file (null = keep the current one, or room 01's by default).
    public static void Run(string tasksPath)
    {
        var root = GameObject.Find("HabitatModule");
        if (root == null)
        {
            EditorUtility.DisplayDialog("HabitatVLM", "No 'HabitatModule' in the open scene. Open Habitat.unity (or run Build Habitat Room) first.", "OK");
            return;
        }

        var report = new List<string>();
        // every object that describes itself + legacy room-01 names not set up yet
        var targets = new Dictionary<Transform, InteractableKind>();
        foreach (var existing in root.GetComponentsInChildren<Interactable>(true))
            targets[existing.transform] = existing.kind;
        foreach (var kv in Kinds)
        {
            var t = FindDeep(root.transform, kv.Key);
            if (t != null && !targets.ContainsKey(t)) targets[t] = kv.Value;
        }
        if (targets.Count == 0) report.Add("No Interactable objects found under HabitatModule");

        foreach (var kv in targets)
        {
            var t = kv.Key;
            var it = GetOrAdd<Interactable>(t.gameObject);
            Undo.RecordObject(it, "Setup Interactable");
            it.kind = kv.Value;
            it.snapPoint = kv.Value == InteractableKind.Item ? null : t.Find("snap_point");
            EditorUtility.SetDirty(it);

            // containers with a "hinge" child get a door/lid; without one they are open-top (basket)
            if (kv.Value == InteractableKind.Container && t.Find("hinge") != null)
            {
                var cab = GetOrAdd<Cabinet>(t.gameObject);
                Undo.RecordObject(cab, "Setup Cabinet");
                cab.hinge = t.Find("hinge");
                EditorUtility.SetDirty(cab);
                if (cab.hinge == null) report.Add($"{t.name}: no 'hinge' child");
            }
            if (kv.Value != InteractableKind.Item && it.snapPoint == null) report.Add($"{t.name}: no 'snap_point' child");
        }

        var robot = GameObject.Find("robot");
        if (robot == null) report.Add("MISSING 'robot'");
        else
        {
            var skills = GetOrAdd<RobotSkills>(robot);
            skills.gripper = robot.transform.Find("gripper");
            EditorUtility.SetDirty(skills);
            GetOrAdd<KeyboardDebug>(robot);
            var cam = robot.transform.Find("robot_cam");
            if (cam != null) GetOrAdd<CameraCapture>(cam.gameObject);
            if (skills.gripper == null) report.Add("robot: no 'gripper' child");
        }

        // Phase 6: AgentManager with BridgeClient + AgentLoop (BridgeImageTest kept but disabled)
        var mgr = GameObject.Find("AgentManager");
        if (mgr == null)
        {
            mgr = new GameObject("AgentManager");
            Undo.RegisterCreatedObjectUndo(mgr, "Create AgentManager");
        }
        GetOrAdd<BridgeClient>(mgr);
        var loop = GetOrAdd<AgentLoop>(mgr);
        GetOrAdd<SceneReset>(mgr);   // soft room reset (no scene reload)
        GetOrAdd<TaskRunner>(mgr);   // Phase 8: run the whole task file
        GetOrAdd<ControlPanel>(mgr); // sidebar UI with the control modes
        GetOrAdd<ClickControl>(mgr); // mouse input for Click / Point+Say
        if (tasksPath != null || loop.tasksFile == null)
        {
            string path = tasksPath ?? "Assets/Tasks/tasks_room01.json";
            loop.tasksFile = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            EditorUtility.SetDirty(loop);
            if (loop.tasksFile == null) report.Add($"{path} not found (task presets will be empty)");
        }
        var imageTest = mgr.GetComponent<BridgeImageTest>();
        if (imageTest != null && imageTest.enabled)
        {
            Undo.RecordObject(imageTest, "Disable BridgeImageTest");
            imageTest.enabled = false;   // AgentLoop replaces it; tick it again to use the SPACE test
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        string msg = report.Count == 0 ? "All objects set up. Save the scene (Ctrl+S)." : string.Join("\n", report);
        Debug.Log("HabitatVLM setup: " + msg.Replace("\n", " | "));
        if (report.Count > 0) EditorUtility.DisplayDialog("HabitatVLM setup", msg, "OK");
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var r = FindDeep(child, name);
            if (r != null) return r;
        }
        return null;
    }
}
#endif
