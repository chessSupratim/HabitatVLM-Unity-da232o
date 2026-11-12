// BridgeImageTest.cs - Phase 3 checkpoint: send the robot camera image to the VLM and show the reply.
// No robot skills are needed. The robot does not move; this only proves Unity -> bridge -> model works.
//
// Setup: create an empty "AgentManager", add BridgeClient + BridgeImageTest.
//        Add CameraCapture to robot_cam (or leave the field empty: it is found by name).
// Play:  press SPACE (or tick "sendOnStart") to send one frame. Results show in the Game view
//        and the Console; the frame is also saved in bridge/logs/frames/ by the bridge.
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HabitatVLM
{
    public class BridgeImageTest : MonoBehaviour
    {
        public BridgeClient bridge;
        public CameraCapture robotCamera;

        [TextArea] public string task = "Put the red canister in the left cabinet";
        public bool sendOnStart = false;

        // Matches SPEC 5.2 and bridge/config.yaml object_kinds
        static readonly NameKind[] Objects =
        {
            new NameKind { name = "red_canister",   kind = "item" },
            new NameKind { name = "blue_canister",  kind = "item" },
            new NameKind { name = "green_canister", kind = "item" },
            new NameKind { name = "toolbox",        kind = "item" },
            new NameKind { name = "shelf_a",        kind = "surface" },
            new NameKind { name = "table",          kind = "surface" },
            new NameKind { name = "cabinet_left",   kind = "container" },
            new NameKind { name = "cabinet_right",  kind = "container" },
        };

        string status = "Press SPACE to send the robot camera image to the VLM";
        string lastReply = "";
        bool busy;
        int stepIndex;
        string episodeId;

        void Start()
        {
            if (bridge == null) bridge = GetComponent<BridgeClient>();
            if (robotCamera == null)
            {
                var camGo = GameObject.Find("robot_cam");
                if (camGo != null)
                {
                    // (Unity objects must not use ?? - a missing component is a "fake null")
                    robotCamera = camGo.GetComponent<CameraCapture>();
                    if (robotCamera == null) robotCamera = camGo.AddComponent<CameraCapture>();
                }
            }
            if (bridge == null || robotCamera == null)
            {
                status = "Missing BridgeClient or robot_cam/CameraCapture";
                Debug.LogError("BridgeImageTest: " + status);
                enabled = false;
                return;
            }
            if (sendOnStart) StartCoroutine(SendOne());
        }

        void Update()
        {
            if (!busy && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                StartCoroutine(SendOne());
        }

        IEnumerator SendOne()
        {
            busy = true;
            string error = null;

            // 1. health
            status = "Checking bridge...";
            HealthResponse health = null;
            yield return bridge.Health(h => health = h, e => error = e);
            if (Fail(error)) yield break;

            // 2. episode (once per Play session)
            if (string.IsNullOrEmpty(episodeId))
            {
                yield return bridge.StartEpisode(new EpisodeStartRequest
                {
                    task = task,
                    scene_objects = Objects.Select(o => o.name).ToArray(),
                    object_kinds = Objects,
                    max_steps = 15,
                    task_id = "IMAGE_TEST",
                }, r => episodeId = r.episode_id, e => error = e);
                if (Fail(error)) yield break;
            }

            // 3. capture
            byte[] jpg = null;
            yield return robotCamera.Capture(b => jpg = b);
            if (jpg == null) { Fail("Camera capture returned nothing"); yield break; }

            // 4. step
            status = $"Waiting for {health.backend} ({health.model})...";
            float t0 = Time.realtimeSinceStartup;
            StepResponse reply = null;
            yield return bridge.Step(new StepRequest
            {
                episode_id = episodeId,
                step = stepIndex++,
                image_b64 = System.Convert.ToBase64String(jpg),
                robot_state = new RobotStateDto
                {
                    containers = new[]
                    {
                        new NameState { name = "cabinet_left",  state = "closed" },
                        new NameState { name = "cabinet_right", state = "closed" },
                    },
                },
            }, r => reply = r, e => error = e);
            if (Fail(error)) yield break;

            float rtt = (Time.realtimeSinceStartup - t0) * 1000f;
            lastReply = $"{reply}\n\"{reply.thought}\"\nvalid={reply.meta.valid}  model={reply.meta.latency_ms} ms  " +
                        $"round-trip={rtt:F0} ms  image={jpg.Length / 1024} KB" +
                        (string.IsNullOrEmpty(reply.meta.error) ? "" : $"\nerror: {reply.meta.error}");
            status = $"Reply from {reply.meta.backend} - press SPACE to send again";
            Debug.Log("BridgeImageTest: " + lastReply.Replace("\n", " | "));
            busy = false;
        }

        bool Fail(string error)
        {
            if (error == null) return false;
            status = "ERROR - is the bridge running? (see Console)";
            Debug.LogError("BridgeImageTest: " + error);
            busy = false;
            return true;
        }

        void OnApplicationQuit()
        {
            if (string.IsNullOrEmpty(episodeId)) return;
            // Fire-and-forget so the episode is logged in episodes.csv
            var req = new UnityEngine.Networking.UnityWebRequest(bridge.baseUrl + "/episode/end", "POST");
            var body = JsonUtility.ToJson(new EpisodeEndRequest
                { episode_id = episodeId, success = false, steps = stepIndex, reason = "image test" });
            req.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.SetRequestHeader("Content-Type", "application/json");
            req.SendWebRequest();
        }

        void OnGUI()
        {
            var tex = robotCamera != null ? robotCamera.Texture : null;
            const int size = 256;
            if (tex != null) GUI.DrawTexture(new Rect(10, 10, size, size), tex);
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 14 };
            GUI.Box(new Rect(10, size + 20, 520, 110), status + "\n\n" + lastReply, style);
        }
    }
}
