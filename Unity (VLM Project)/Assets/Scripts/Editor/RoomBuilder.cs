// RoomBuilder.cs - build a habitat room scene from a JSON layout (PLAN Phase 9, SPEC 5.6).
//
// Menus:
//   HabitatVLM > Build Rooms from Layouts       builds every Assets/Rooms/room_*.json
//   HabitatVLM > Build Room from Layout...      pick one layout file
//
// Each layout becomes its own scene  Assets/Scenes/Rooms/<room>.unity  with: floor, walls and a
// ceiling (the front wall and the ceiling are on the "HiddenFromOverview" layer: the robot camera
// sees a closed room, the overview camera looks in from above), furniture, items, cabinets (hinged
// doors), the robot with its camera, lights, the overview camera, a baked NavMesh, and the
// AgentManager (via HabitatSetup), with the room's own task file. Rebuilding overwrites the scene.
//
// Layout format (all distances in metres, room centred on the origin, Y up):
//   room, title, width, depth, height, floorColor, wallColor, tasks (task-file path)
//   robot: [x, z, lookAtX, lookAtZ]
//   objects[]: name, type = surface | container | item | obstacle, color (#rrggbb)
//     surface/obstacle: pos [x, z] (footprint centre), size [w, h, d]
//     container:        pos, size [w, h, d] (default 1 x 1.5 x 0.6), facing = south|north|east|west
//                       (direction the door faces; put the back against a wall), doorColor
//     item:             shape = cube|cylinder, size [w, h, d], on = surface/container it starts on,
//                       pos [x, z] (optional; default = centre of "on")
//   styles (optional "style" field):
//     surface:   block (default) | table (top + 4 legs; doorColor = legs) | bed (frame, mattress,
//                blanket = doorColor, headboard at the +Z end)
//     container: cabinet (default, hinged door) | tub (lid hinged at the back, swings up) |
//                basket (open top: always open, no door)
//     item:      plain (default, uses shape) | plant (pot = color + leaves) | books (stack of 3)
//     decor:     box | window (frame + glass + curtains = doorColor) | picture | door | lamp
//                (desk lamp with a warm light) | plant | books.  pos [x, z], y = base height,
//                size, facing (the side you see). Decor has no colliders and is never an object
//                the VLM can use.
//   lightColor / lightIntensity: room lights (default cool white, 3)
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using HabitatVLM;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class RoomBuilder
{
    [Serializable]
    public class RoomLayout
    {
        public string room, title, tasks;
        public float width = 8, depth = 6, height = 3;
        public string floorColor = "#40454d", wallColor = "#dde0e6";
        public string lightColor = "#e6f2ff";
        public float lightIntensity = 3f;
        public float[] robot;
        public LayoutObject[] objects;
    }

    [Serializable]
    public class LayoutObject
    {
        public string name, type, shape, on, facing, color, doorColor, style;
        public float[] pos, size;
        public float y;          // decor: base height above the floor
        public float light;      // decor lamp: light intensity (0 = default)
    }

    const string LayoutDir = "Assets/Rooms";
    const string SceneDir = "Assets/Scenes/Rooms";
    const string MatDir = "Assets/Materials/Rooms";
    const string HiddenLayerName = "HiddenFromOverview";

    // ------------------------------------------------------------------ menus
    [MenuItem("HabitatVLM/Build Rooms from Layouts")]
    public static void BuildAll()
    {
        var files = Directory.GetFiles(LayoutDir, "room_*.json");
        Array.Sort(files);
        if (files.Length == 0) { EditorUtility.DisplayDialog("HabitatVLM", $"No room_*.json in {LayoutDir}.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var built = new List<string>();
        foreach (var f in files)
        {
            string scene = Build(f.Replace('\\', '/'));
            if (scene != null) built.Add(scene);
        }
        if (built.Count > 0) EditorSceneManager.OpenScene(built[0]);
        EditorUtility.DisplayDialog("HabitatVLM", "Built:\n" + string.Join("\n", built) +
            "\n\nOpen a room from Assets/Scenes/Rooms and press Play.", "OK");
    }

    [MenuItem("HabitatVLM/Build Room from Layout...")]
    public static void BuildOne()
    {
        string path = EditorUtility.OpenFilePanel("Room layout", LayoutDir, "json");
        if (string.IsNullOrEmpty(path)) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string rel = "Assets" + path.Substring(Application.dataPath.Length);
        string scene = Build(rel);
        if (scene != null) EditorSceneManager.OpenScene(scene);
    }

    // ------------------------------------------------------------------ build one room
    /// Build the scene for one layout file. Returns the scene path, or null on error.
    public static string Build(string layoutPath)
    {
        RoomLayout L;
        try { L = JsonUtility.FromJson<RoomLayout>(File.ReadAllText(layoutPath)); }
        catch (Exception e) { Debug.LogError($"RoomBuilder: cannot read {layoutPath}: {e.Message}"); return null; }
        if (L == null || string.IsNullOrEmpty(L.room) || L.objects == null)
        { Debug.LogError($"RoomBuilder: {layoutPath} needs 'room' and 'objects'."); return null; }

        EnsureFolder(SceneDir);
        EnsureFolder(MatDir);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var root = new GameObject("HabitatModule");
        var structure = Child("Structure", root.transform, Vector3.zero);
        var furniture = Child("Furniture", root.transform, Vector3.zero);
        var items = Child("Items", root.transform, Vector3.zero);
        var lights = Child("Lights", root.transform, Vector3.zero);
        var decor = Child("Decor", root.transform, Vector3.zero);

        // floor + walls. The front wall (-Z) is seen by the robot camera only (see HideFromOverview).
        float W = L.width, D = L.depth, H = L.height, t = 0.2f;
        int hiddenLayer = EnsureLayer(HiddenLayerName);
        var wall = Mat(L.wallColor);
        Prim(PrimitiveType.Cube, "floor", structure, new Vector3(0, -0.05f, 0), new Vector3(W, 0.1f, D), Mat(L.floorColor));
        Prim(PrimitiveType.Cube, "wall_back", structure, new Vector3(0, H / 2, D / 2), new Vector3(W, H, t), wall);
        Prim(PrimitiveType.Cube, "wall_left", structure, new Vector3(-W / 2, H / 2, 0), new Vector3(t, H, D), wall);
        Prim(PrimitiveType.Cube, "wall_right", structure, new Vector3(W / 2, H / 2, 0), new Vector3(t, H, D), wall);
        var front = Prim(PrimitiveType.Cube, "wall_front", structure, new Vector3(0, H / 2, -D / 2), new Vector3(W, H, t), wall);
        HideFromOverview(front, hiddenLayer);

        // furniture first (items need their hosts), then items
        var hosts = new Dictionary<string, Transform>();
        var problems = new List<string>();
        foreach (var o in L.objects)
        {
            if (o.type == "item") continue;
            Vector3 p = Pos(o);
            switch (o.type)
            {
                case "surface":  hosts[o.name] = BuildSurface(o, furniture, p); break;
                case "container": hosts[o.name] = BuildContainer(o, furniture, p); break;
                case "obstacle": BuildObstacle(o, furniture, p); break;
                case "decor":    BuildDecor(o, decor, p); break;
                default: problems.Add($"{o.name}: unknown type '{o.type}'"); break;
            }
        }
        foreach (var o in L.objects)
        {
            if (o.type != "item") continue;
            if (string.IsNullOrEmpty(o.on) || !hosts.TryGetValue(o.on, out var host))
            { problems.Add($"{o.name}: 'on' must name a surface or container in this layout"); continue; }
            BuildItem(o, items, host);
        }

        // lights: a grid of point lights under the ceiling + a soft directional fill
        int nx = Mathf.Max(1, Mathf.CeilToInt(W / 4f)), nz = Mathf.Max(1, Mathf.CeilToInt(D / 4f));
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                PointLight($"light_{i}_{j}", lights, new Vector3(-W / 2 + W * (i + 0.5f) / nx, H - 0.3f, -D / 2 + D * (j + 0.5f) / nz),
                           Hex(L.lightColor, new Color(0.90f, 0.95f, 1f)), L.lightIntensity > 0 ? L.lightIntensity : 3f, 7f);
        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.4f;
        sun.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(50, -30, 0);

        // robot (outside HabitatModule so the NavMesh bake ignores it)
        float[] r = L.robot != null && L.robot.Length >= 2 ? L.robot : new float[] { 0, 0, 0, 1 };
        BuildRobot(new Vector3(r[0], 0, r[1]), r.Length >= 4 ? new Vector3(r[2], 0, r[3]) : Vector3.zero);

        // overview camera: same view as room 01, scaled to the room size
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        float s = Mathf.Max(W / 8f, D / 6f);
        camGo.transform.position = new Vector3(0, 9f * s, -D / 2 - 4.5f * s);
        camGo.transform.LookAt(new Vector3(0, 0, 0.1f * D));
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        if (hiddenLayer >= 0) cam.cullingMask &= ~(1 << hiddenLayer);

        // Furniture and items are never walkable: the default agent can "step" 0.75 m, so without
        // this a 0.75 m table top became part of the floor and the robot walked over the table.
        foreach (var group in new[] { furniture, items, decor })
        {
            var mod = group.gameObject.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = NavMesh.GetAreaFromName("Not Walkable");
        }

        // NavMesh: bake now and save the data next to the scene
        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.BuildNavMesh();
        string navPath = $"{SceneDir}/{L.room}_NavMesh.asset";
        AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);

        // ceiling AFTER the bake (so no walkable area is made on top of it); robot camera only
        var ceiling = Prim(PrimitiveType.Cube, "ceiling", structure, new Vector3(0, H + 0.05f, 0), new Vector3(W, 0.1f, D), wall);
        HideFromOverview(ceiling, hiddenLayer);

        // components: Interactables are already on the objects; Setup adds robot skills, agent, UI
        string tasks = !string.IsNullOrEmpty(L.tasks) ? L.tasks : $"Assets/Tasks/tasks_{L.room.Replace("_", "")}.json";
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(tasks) == null) problems.Add($"task file {tasks} not found");
        HabitatSetup.Run(tasks);

        string scenePath = $"{SceneDir}/{L.room}.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AddToBuildSettings(scenePath);
        AssetDatabase.SaveAssets();

        string msg = $"RoomBuilder: built {L.room} ({L.title}) -> {scenePath}";
        if (problems.Count > 0) Debug.LogWarning(msg + "\n  " + string.Join("\n  ", problems));
        else Debug.Log(msg);
        return scenePath;
    }

    // ------------------------------------------------------------------ pieces
    static Transform BuildSurface(LayoutObject o, Transform parent, Vector3 p)
    {
        Vector3 size = Size(o, new Vector3(1.2f, 0.8f, 0.8f));
        float w = size.x, h = size.y, d = size.z;
        var go = Child(o.name, parent, p);
        go.localRotation = Quaternion.Euler(0, FacingAngle(o.facing), 0);
        float top = h;
        switch (o.style)
        {
            case "table":   // top slab + 4 legs
            {
                Prim(PrimitiveType.Cube, "top", go, new Vector3(0, h - 0.02f, 0), new Vector3(w, 0.04f, d), Mat(o.color));
                var legMat = Mat(string.IsNullOrEmpty(o.doorColor) ? "#2b2b2b" : o.doorColor);
                float lx = w / 2 - 0.06f, lz = d / 2 - 0.06f;
                foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    Prim(PrimitiveType.Cube, "leg", go, new Vector3(sx * lx, (h - 0.04f) / 2, sz * lz), new Vector3(0.05f, h - 0.04f, 0.05f), legMat);
                break;
            }
            case "bed":     // frame + mattress + blanket over the foot end + headboard at +Z
            {
                float frame = h * 0.55f, mattress = h - frame;
                Prim(PrimitiveType.Cube, "frame", go, new Vector3(0, frame / 2, 0), new Vector3(w, frame, d), Mat(o.color));
                Prim(PrimitiveType.Cube, "mattress", go, new Vector3(0, frame + mattress / 2, 0), new Vector3(w - 0.06f, mattress, d - 0.08f), Mat("#dcdcdc"));
                Prim(PrimitiveType.Cube, "blanket", go, new Vector3(0, h + 0.005f, -d * 0.15f), new Vector3(w - 0.02f, 0.012f, d * 0.68f),
                     Mat(string.IsNullOrEmpty(o.doorColor) ? "#3a3a40" : o.doorColor));
                Prim(PrimitiveType.Cube, "headboard", go, new Vector3(0, 0.5f, d / 2 - 0.04f), new Vector3(w, 1.0f, 0.08f), Mat(o.color));
                top = h + 0.01f;
                break;
            }
            default:
                Prim(PrimitiveType.Cube, "body", go, new Vector3(0, h / 2, 0), size, Mat(o.color));
                break;
        }
        var snap = Child("snap_point", go, new Vector3(0, top, 0));
        var it = go.gameObject.AddComponent<Interactable>();
        it.kind = InteractableKind.Surface;
        it.snapPoint = snap;
        return go;
    }

    static void BuildObstacle(LayoutObject o, Transform parent, Vector3 p)
    {
        Vector3 size = Size(o, Vector3.one);
        var go = Child(o.name, parent, p);
        Prim(PrimitiveType.Cube, "body", go, new Vector3(0, size.y / 2, 0), size, Mat(o.color));
    }

    /// Hollow cabinet: back, sides, top, bottom; door on a hinge at its left edge (seen from the
    /// front). Built facing -Z, then turned so the door faces o.facing.
    static Transform BuildContainer(LayoutObject o, Transform parent, Vector3 p)
    {
        Vector3 size = Size(o, new Vector3(1f, 1.5f, 0.6f));
        float W = size.x, H = size.y, D = size.z, T = 0.05f;
        var body = Mat(o.color);
        var c = Child(o.name, parent, p);
        c.localRotation = Quaternion.Euler(0, FacingAngle(o.facing), 0);
        if (o.style == "tub" || o.style == "basket") return BuildBox(o, c, size, body);

        Prim(PrimitiveType.Cube, "panel_back", c, new Vector3(0, H / 2, D / 2 - T / 2), new Vector3(W, H, T), body);
        Prim(PrimitiveType.Cube, "panel_left", c, new Vector3(-W / 2 + T / 2, H / 2, 0), new Vector3(T, H, D), body);
        Prim(PrimitiveType.Cube, "panel_right", c, new Vector3(W / 2 - T / 2, H / 2, 0), new Vector3(T, H, D), body);
        Prim(PrimitiveType.Cube, "panel_top", c, new Vector3(0, H - T / 2, 0), new Vector3(W, T, D), body);
        Prim(PrimitiveType.Cube, "panel_bottom", c, new Vector3(0, T / 2, 0), new Vector3(W, T, D), body);

        var hinge = Child("hinge", c, new Vector3(-W / 2, H / 2, -D / 2 - 0.03f));
        Prim(PrimitiveType.Cube, "door", hinge, new Vector3(W / 2, 0, 0), new Vector3(W - 0.02f, H - 0.04f, 0.04f),
             Mat(string.IsNullOrEmpty(o.doorColor) ? o.color : o.doorColor));
        var snap = Child("snap_point", c, new Vector3(0, T, 0));

        var it = c.gameObject.AddComponent<Interactable>();
        it.kind = InteractableKind.Container;
        it.snapPoint = snap;
        var cab = c.gameObject.AddComponent<Cabinet>();
        cab.hinge = hinge;
        return c;
    }

    /// Open-top box (basket) or box with a lid hinged on its back top edge (tub).
    static Transform BuildBox(LayoutObject o, Transform c, Vector3 size, Material body)
    {
        float W = size.x, H = size.y, D = size.z, T = 0.03f;
        Prim(PrimitiveType.Cube, "panel_bottom", c, new Vector3(0, T / 2, 0), new Vector3(W, T, D), body);
        Prim(PrimitiveType.Cube, "panel_back", c, new Vector3(0, H / 2, D / 2 - T / 2), new Vector3(W, H, T), body);
        Prim(PrimitiveType.Cube, "panel_front", c, new Vector3(0, H / 2, -D / 2 + T / 2), new Vector3(W, H, T), body);
        Prim(PrimitiveType.Cube, "panel_left", c, new Vector3(-W / 2 + T / 2, H / 2, 0), new Vector3(T, H, D), body);
        Prim(PrimitiveType.Cube, "panel_right", c, new Vector3(W / 2 - T / 2, H / 2, 0), new Vector3(T, H, D), body);
        var snap = Child("snap_point", c, new Vector3(0, T, 0));

        var it = c.gameObject.AddComponent<Interactable>();
        it.kind = InteractableKind.Container;
        it.snapPoint = snap;

        if (o.style == "tub")
        {
            var hinge = Child("hinge", c, new Vector3(0, H, D / 2));
            Prim(PrimitiveType.Cube, "lid", hinge, new Vector3(0, 0.02f, -D / 2), new Vector3(W + 0.02f, 0.04f, D + 0.02f),
                 Mat(string.IsNullOrEmpty(o.doorColor) ? o.color : o.doorColor));
            var cab = c.gameObject.AddComponent<Cabinet>();
            cab.hinge = hinge;
            cab.axis = Vector3.right;   // lid swings up and back
            cab.openAngle = 105f;
        }
        return c;
    }

    /// Non-interactive dressing: no colliders, not an Interactable (the VLM never sees its name).
    static void BuildDecor(LayoutObject o, Transform parent, Vector3 p)
    {
        Vector3 size = Size(o, new Vector3(0.3f, 0.3f, 0.3f));
        float w = size.x, h = size.y;
        var g = Child(o.name, parent, new Vector3(p.x, o.y, p.z));
        g.localRotation = Quaternion.Euler(0, FacingAngle(o.facing), 0);
        var main = Mat(o.color);
        var second = Mat(string.IsNullOrEmpty(o.doorColor) ? "#2e2f33" : o.doorColor);
        switch (o.style)
        {
            case "window":
                Prim(PrimitiveType.Cube, "frame", g, new Vector3(0, h / 2, 0), new Vector3(w, h, 0.06f), main);
                Prim(PrimitiveType.Cube, "glass", g, new Vector3(0, h / 2, -0.005f), new Vector3(w - 0.12f, h - 0.12f, 0.06f), Mat("#cfe6f5"));
                Prim(PrimitiveType.Cube, "bar", g, new Vector3(0, h / 2, -0.01f), new Vector3(0.04f, h - 0.12f, 0.06f), main);
                foreach (int sx in new[] { -1, 1 })
                    Prim(PrimitiveType.Cube, "curtain", g, new Vector3(sx * (w / 2 + 0.12f), h / 2 - 0.1f, -0.1f), new Vector3(0.4f, h + 0.5f, 0.08f), second);
                break;
            case "picture":
                Prim(PrimitiveType.Cube, "frame", g, new Vector3(0, h / 2, 0), new Vector3(w, h, 0.04f), main);
                Prim(PrimitiveType.Cube, "print", g, new Vector3(0, h / 2, -0.005f), new Vector3(w - 0.08f, h - 0.08f, 0.04f), second);
                break;
            case "door":
                Prim(PrimitiveType.Cube, "panel", g, new Vector3(0, h / 2, 0), new Vector3(w, h, 0.06f), main);
                Prim(PrimitiveType.Sphere, "knob", g, new Vector3(w / 2 - 0.1f, h * 0.48f, -0.06f), Vector3.one * 0.07f, Mat("#b0b3b8"));
                break;
            case "lamp":
            {
                Prim(PrimitiveType.Cylinder, "base", g, new Vector3(0, 0.01f, 0), new Vector3(0.14f, 0.01f, 0.14f), main);
                Prim(PrimitiveType.Cube, "pole", g, new Vector3(0, h / 2, 0), new Vector3(0.025f, h, 0.025f), main);
                Prim(PrimitiveType.Cylinder, "shade", g, new Vector3(0, h, -0.06f), new Vector3(0.18f, 0.06f, 0.18f), main);
                var l = Child("bulb", g, new Vector3(0, h - 0.08f, -0.06f)).gameObject.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 3f;
                l.intensity = o.light > 0 ? o.light : 2f;
                l.color = new Color(1f, 0.80f, 0.55f);
                l.shadows = LightShadows.None;
                break;
            }
            case "plant":
                Prim(PrimitiveType.Cylinder, "pot", g, new Vector3(0, h * 0.2f, 0), new Vector3(w * 0.6f, h * 0.2f, w * 0.6f), main);
                Prim(PrimitiveType.Sphere, "leaves", g, new Vector3(0, h * 0.68f, 0), new Vector3(w, h * 0.6f, w), Mat("#3f8f3a"));
                break;
            case "books":
                BookStack(g, size, o.color);
                break;
            default:   // box
                Prim(PrimitiveType.Cube, "body", g, new Vector3(0, h / 2, 0), size, main);
                break;
        }
        foreach (var col in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
    }

    static void BookStack(Transform g, Vector3 size, string color, float yBase = 0f)
    {
        string[] colors = { color, "#2a4d8f", "#d9c79e" };
        float bh = size.y / 3f;
        for (int i = 0; i < 3; i++)
        {
            var b = Prim(PrimitiveType.Cube, "book", g, new Vector3((i - 1) * 0.01f, yBase + bh * (i + 0.5f), 0),
                         new Vector3(size.x * (1f - 0.06f * i), bh * 0.95f, size.z * (1f - 0.05f * i)), Mat(colors[i]));
            b.transform.localRotation = Quaternion.Euler(0, (i - 1) * 6f, 0);
        }
    }

    static void BuildItem(LayoutObject o, Transform parent, Transform host)
    {
        Vector3 size = Size(o, new Vector3(0.2f, 0.3f, 0.2f));
        var snap = host.Find("snap_point");
        float baseY = snap != null ? snap.position.y : 0f;
        Vector3 xz = o.pos != null && o.pos.Length >= 2 ? new Vector3(o.pos[0], 0, o.pos[1])
                   : (snap != null ? snap.position : host.position);
        var at = new Vector3(xz.x, baseY + size.y / 2 + 0.005f, xz.z);

        GameObject go;
        if (o.style == "plant" || o.style == "books")
        {
            // composite item: empty root at the centre (pick/place move the root), parts below it
            go = Child(o.name, parent, at).gameObject;
            float hh = size.y / 2;
            if (o.style == "plant")
            {
                Prim(PrimitiveType.Cylinder, "pot", go.transform, new Vector3(0, -hh + size.y * 0.2f, 0),
                     new Vector3(size.x * 0.6f, size.y * 0.2f, size.z * 0.6f), Mat(o.color));
                Prim(PrimitiveType.Sphere, "leaves", go.transform, new Vector3(0, -hh + size.y * 0.68f, 0),
                     new Vector3(size.x, size.y * 0.6f, size.z), Mat("#3f8f3a"));
            }
            else BookStack(go.transform, size, o.color, -hh);
        }
        else
        {
            bool cyl = o.shape == "cylinder";
            // Unity's cylinder is 2 units tall at scale 1
            var scale = cyl ? new Vector3(size.x, size.y / 2, size.z) : size;
            go = Prim(cyl ? PrimitiveType.Cylinder : PrimitiveType.Cube, o.name, parent, at, scale, Mat(o.color));
        }
        go.AddComponent<Interactable>().kind = InteractableKind.Item;   // location found at Start (raycast down)
    }

    static void BuildRobot(Vector3 pos, Vector3 lookAt)
    {
        var white = Mat("#f2f2f2");
        var robot = new GameObject("robot");
        robot.transform.position = pos;
        var look = lookAt - pos; look.y = 0;
        if (look.sqrMagnitude > 1e-4f) robot.transform.rotation = Quaternion.LookRotation(look);

        Prim(PrimitiveType.Capsule, "body", robot.transform, new Vector3(0, 0.5f, 0), new Vector3(0.6f, 0.5f, 0.6f), white);
        var nose = Prim(PrimitiveType.Cube, "front_marker", robot.transform, new Vector3(0, 0.8f, 0.3f), new Vector3(0.2f, 0.1f, 0.1f), Mat("#0d0d0d"));
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        Child("gripper", robot.transform, new Vector3(0, 1.0f, 0.45f));

        var camGo = new GameObject("robot_cam");
        camGo.transform.SetParent(robot.transform, false);
        camGo.transform.localPosition = new Vector3(0, 1.3f, 0.25f);
        camGo.transform.localEulerAngles = new Vector3(20, 0, 0);
        var rc = camGo.AddComponent<Camera>();
        rc.fieldOfView = 70;
        rc.nearClipPlane = 0.05f;
        rc.targetTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>("Assets/Textures/RobotCamRT.renderTexture");
        if (rc.targetTexture == null)
        {
            EnsureFolder("Assets/Textures");
            var rt = new RenderTexture(512, 512, 24);
            AssetDatabase.CreateAsset(rt, "Assets/Textures/RobotCamRT.renderTexture");
            rc.targetTexture = rt;
        }

        var agent = robot.AddComponent<NavMeshAgent>();
        agent.speed = 1.5f;
        agent.angularSpeed = 240f;
        agent.acceleration = 4f;
        agent.stoppingDistance = 0.6f;
        agent.radius = 0.35f;
        agent.height = 1.0f;
    }

    static void PointLight(string name, Transform parent, Vector3 pos, Color color, float intensity, float range)
    {
        var l = Child(name, parent, pos).gameObject.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = color;
        l.shadows = LightShadows.None;
    }

    static Color Hex(string hex, Color fallback) =>
        !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;

    // ------------------------------------------------------------------ helpers
    /// Visible to the robot camera, not to the overview camera (falls back to an invisible
    /// collider if no free layer exists).
    static void HideFromOverview(GameObject go, int layer)
    {
        if (layer >= 0) go.layer = layer;
        else go.GetComponent<MeshRenderer>().enabled = false;
    }

    /// Find or create a user layer by name (TagManager). Returns -1 if all user layers are taken.
    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;
        var tm = new SerializedObject(assets[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < 32 && i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(p.stringValue)) continue;
            p.stringValue = name;
            tm.ApplyModifiedProperties();
            return i;
        }
        Debug.LogWarning("RoomBuilder: no free layer; the front wall will be an invisible collider.");
        return -1;
    }

    /// Door direction -> Y rotation of a cabinet built facing -Z.
    static float FacingAngle(string facing)
    {
        switch ((facing ?? "south").ToLowerInvariant())
        {
            case "north": return 180f;
            case "east":  return -90f;
            case "west":  return 90f;
            default:      return 0f;   // south (-Z)
        }
    }

    static Vector3 Pos(LayoutObject o) =>
        o.pos != null && o.pos.Length >= 2 ? new Vector3(o.pos[0], 0, o.pos[1]) : Vector3.zero;

    static Vector3 Size(LayoutObject o, Vector3 fallback) =>
        o.size != null && o.size.Length >= 3 ? new Vector3(o.size[0], o.size[1], o.size[2]) : fallback;

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = scale;
        if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
        return g;
    }

    static Transform Child(string name, Transform parent, Vector3 localPos)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        return g.transform;
    }

    /// One material asset per colour, shared by all rooms.
    static Material Mat(string hex)
    {
        if (string.IsNullOrEmpty(hex)) hex = "#8c9096";
        if (!ColorUtility.TryParseHtmlString(hex, out var color)) color = Color.magenta;
        string path = $"{MatDir}/C_{hex.TrimStart('#').ToLowerInvariant()}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            AssetDatabase.CreateAsset(mat, path);
        }
        return mat;
    }

    static void AddToBuildSettings(string scenePath)
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (list.Exists(s => s.path == scenePath)) return;
        list.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
