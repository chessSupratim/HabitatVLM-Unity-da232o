using System;
// AgentLoop.cs - Phase 6: the VLM drives the robot (SPEC 1, 7, 11).
// Attach to "AgentManager" (with BridgeClient). The on-screen UI is ControlPanel (sidebar with
// the control modes); this class is the episode engine used by the Tasks, Text and Point modes.
//
// Loop:  health -> /episode/start (objects + kinds found in THIS scene)
//        repeat { capture camera -> /step -> execute skill -> remember result as last_result }
//        until the model says "done", Stop is pressed, or max steps -> success check -> /episode/end
//
// Nothing here names a specific object: the object list comes from the Interactables in the
// loaded scene and the task presets come from a per-room task file (SPEC 5.6).
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HabitatVLM
{
    public class EpisodeOutcome
    {
        public string taskId;
        public bool success;
        public int steps;
        public string reason;
        public bool infrastructureError;   // bridge/model unreachable: stop a suite run
    }

    public class AgentLoop : MonoBehaviour
    {
        [Header("References (found automatically if empty)")]
        public BridgeClient bridge;
        public CameraCapture robotCamera;
        public RobotSkills robot;

        [Header("Tasks")]
        [Tooltip("Per-room task presets, e.g. Assets/Tasks/tasks_room01.json")]
        public TextAsset tasksFile;
        [TextArea] public string freeTask = "Put the red canister in the left cabinet";

        [Header("Episode")]
        public int maxSteps = 15;
        public float pauseBetweenSteps = 0.3f;
        [Tooltip("Retries when the bridge cannot be reached during a step.")]
        public int networkRetries = 2;

        TaskFile tasks;
        int presetIndex;
        string taskText = "";
        bool taskEdited;

        bool running, stopRequested;
        public bool Running => running;
        public TaskFile Tasks => tasks;
        System.Action<EpisodeOutcome> onDone;
        EpisodeOutcome outcome;
        string status = "Ready. Choose a mode in the panel.";
        string backendInfo = "";
        string lastThought = "";
        readonly List<string> log = new List<string>();

        // read by ControlPanel
        public string Status { get => status; set => status = value; }
        public string BackendInfo => backendInfo;
        public string LastThought => lastThought;
        public IReadOnlyList<string> Log => log;
        public int LogVersion { get; private set; }
        public RobotSkills Robot => robot;
        public CameraCapture RobotCamera => robotCamera;
        public TaskRunner Runner => runner;
        public bool Busy => running || (runner != null && runner.Running);

        TaskRunner runner;
        SceneReset sceneReset;

        TaskSpec CurrentPreset => tasks != null && tasks.tasks.Length > 0 ? tasks.tasks[presetIndex] : null;

        void Start()
        {
            // Keep running when Unity loses focus (e.g. you switch to the Kaggle tab);
            // otherwise Play mode pauses and episodes stall for minutes.
            Application.runInBackground = true;
            if (bridge == null) bridge = GetComponent<BridgeClient>();
            if (robot == null) robot = FindAnyObjectByType<RobotSkills>();
            if (robotCamera == null && robot != null)
            {
                var cam = robot.transform.Find("robot_cam");
                if (cam != null)
                {
                    robotCamera = cam.GetComponent<CameraCapture>();
                    if (robotCamera == null) robotCamera = cam.gameObject.AddComponent<CameraCapture>();
                }
            }
            if (bridge == null || robot == null || robotCamera == null)
            {
                status = "Setup incomplete: need BridgeClient (here), RobotSkills (robot) and robot_cam.";
                Debug.LogError("AgentLoop: " + status);
                enabled = false;
                return;
            }
            tasks = TaskFile.Parse(tasksFile);
            runner = GetComponent<TaskRunner>();
            sceneReset = GetComponent<SceneReset>();
            if (CurrentPreset != null) { taskText = CurrentPreset.task; taskEdited = false; }
            else { taskText = freeTask; taskEdited = true; }

            // the UI and mouse input live in their own components; add them if setup was not re-run
            if (GetComponent<ControlPanel>() == null) gameObject.AddComponent<ControlPanel>();
            if (GetComponent<ClickControl>() == null) gameObject.AddComponent<ClickControl>();
        }

        // ============================================================ task presets (Tasks mode)
        public string TaskText
        {
            get => taskText;
            set
            {
                if (value == taskText) return;
                taskText = value;
                taskEdited = CurrentPreset == null || value != CurrentPreset.task;
            }
        }

        public string PresetLabel => CurrentPreset == null ? "free task" :
            (taskEdited ? "free task (edited)" : $"{CurrentPreset.id}  [{CurrentPreset.tier}]  {presetIndex + 1}/{tasks.tasks.Length}");

        // ============================================================ episode
        public void StartEpisode()
        {
            if (!running && !string.IsNullOrWhiteSpace(taskText))
                StartCoroutine(RunEpisode(taskEdited ? null : CurrentPreset, taskText.Trim()));
        }

        /// Text / Point+Say modes: run a typed command from the room's current state (no reset).
        /// taskId is only a log label ("TEXT", "POINT"); success is unverified.
        public bool StartCommand(string command, string taskId)
        {
            if (Busy || string.IsNullOrWhiteSpace(command)) return false;
            StartCoroutine(RunEpisode(null, command.Trim(), null, taskId));
            return true;
        }

        public void RequestStop()
        {
            stopRequested = true;
            if (runner != null) runner.RequestStop();
        }

        /// Run one episode. spec may be null (free-form task, success unverified).
        public IEnumerator RunEpisode(TaskSpec spec, string task, System.Action<EpisodeOutcome> done = null, string freeId = "FREE")
        {
            running = true;
            onDone = done;
            string taskId = spec != null ? spec.id : freeId;
            outcome = new EpisodeOutcome { taskId = taskId, reason = "", infrastructureError = true };
            if (spec != null) { taskText = spec.task; taskEdited = false; }
            stopRequested = false;
            log.Clear();
            lastThought = "";
            var keyboard = robot.GetComponent<KeyboardDebug>();
            bool keyboardWasEnabled = keyboard != null && keyboard.enabled;
            if (keyboard != null) keyboard.enabled = false;   // no manual commands mid-episode

            string error = null;

            // 1. bridge health
            status = "Checking bridge...";
            HealthResponse health = null;
            yield return bridge.Health(h => health = h, e => error = e);
            if (health == null)
            {
                Finish("Bridge not reachable. Is it running?  " + error, keyboard, keyboardWasEnabled);
                yield break;
            }
            backendInfo = $"{health.backend} / {health.model}";

            // 2. episode start with the objects found in this scene
            var objects = Interactable.All.Values.Where(i => i != null).OrderBy(i => i.Name).ToList();
            string episodeId = null;
            yield return bridge.StartEpisode(new EpisodeStartRequest
            {
                task = task,
                scene_objects = objects.Select(o => o.Name).ToArray(),
                object_kinds = objects.Select(o => new NameKind { name = o.Name, kind = o.KindName }).ToArray(),
                max_steps = maxSteps,
                task_id = taskId,
            }, r => episodeId = r.episode_id, e => error = e);
            if (episodeId == null)
            {
                Finish("Could not start episode: " + error, keyboard, keyboardWasEnabled);
                yield break;
            }
            AddLog($"Episode {episodeId}: \"{task}\"  ({objects.Count} objects, {backendInfo})");

            // 3. the loop
            var last = new ActionResultDto();
            int steps = 0, backendFailures = 0;
            bool modelDone = false;
            bool modelCannot = false;   // done with a "CANNOT: ..." thought (impossible task)
            string endReason = "";

            for (int step = 0; step < maxSteps; step++)
            {
                if (stopRequested) { endReason = "stopped by user"; break; }

                status = $"Step {step + 1}/{maxSteps}: looking...";
                byte[] jpg = null;
                yield return robotCamera.Capture(b => jpg = b);

                status = $"Step {step + 1}/{maxSteps}: asking {health.model}...";
                StepResponse reply = null;
                var req = new StepRequest
                {
                    episode_id = episodeId,
                    step = step,
                    image_b64 = System.Convert.ToBase64String(jpg),
                    robot_state = robot.GetState(),
                    last_result = last,
                };
                for (int attempt = 0; attempt <= networkRetries && reply == null; attempt++)
                {
                    if (attempt > 0) { status = $"Step {step + 1}: bridge error, retry {attempt}..."; yield return new WaitForSeconds(2f); }
                    error = null;
                    yield return bridge.Step(req, r => reply = r, e => error = e);
                }
                if (reply == null) { endReason = "bridge error: " + error; break; }

                steps = step + 1;
                lastThought = reply.thought;
                string meta = $"{reply.meta.latency_ms} ms" + (reply.meta.valid ? "" : ", INVALID -> fallback");
                AddLog($"{steps}. {reply}   [{meta}]");
                if (!string.IsNullOrEmpty(reply.thought)) AddLog($"    \"{reply.thought}\"");

                // Model backend down (e.g. Kaggle session stopped): don't spin in place, stop clearly
                if (!string.IsNullOrEmpty(reply.meta.error) && reply.meta.error.StartsWith("backend error"))
                {
                    backendFailures++;
                    AddLog($"    model backend error ({backendFailures}/2): {reply.meta.error}");
                    if (backendFailures >= 2)
                    {
                        endReason = $"model backend '{health.backend}' unreachable - is the Kaggle session/tunnel running " +
                                    "and KAGGLE_TUNNEL_URL in bridge/.env current? (or start the bridge with VLM_BACKEND=mock)";
                        break;
                    }
                    // no decision from the model: don't move; tell the bridge the fallback was not executed
                    last = new ActionResultDto { action = reply.action ?? "look_around", success = false,
                                                 reason = "not executed: model backend error" };
                    continue;
                }
                backendFailures = 0;

                if (reply.action == "done")
                {
                    modelDone = true;
                    string th = (reply.thought ?? "").Trim();
                    modelCannot = th.IndexOf("CANNOT:", StringComparison.Ordinal) >= 0;   // models put it anywhere in the thought
                    endReason = modelCannot ? "model said it cannot do this: " + th : "model said done";
                    break;
                }

                status = $"Step {step + 1}/{maxSteps}: {reply}";
                SkillResult result = null;
                yield return robot.Execute(reply.action, reply.target, reply.@object, reply.location, r => result = r);
                AddLog($"    -> {result}");

                last = new ActionResultDto
                {
                    action = reply.action ?? "",
                    target = reply.target ?? "",
                    @object = reply.@object ?? "",
                    location = reply.location ?? "",
                    success = result.success,
                    reason = result.reason ?? "",
                };
                yield return new WaitForSeconds(pauseBetweenSteps);
            }
            if (string.IsNullOrEmpty(endReason)) endReason = $"step limit ({maxSteps}) reached";

            // 4. Unity decides success
            bool success;
            string why;
            if (spec != null && spec.HasCondition) success = SuccessChecker.Check(spec.success, robot, out why);
            else
            {
                success = modelDone && !modelCannot;
                why = modelCannot ? "not done (task not possible)"
                    : "unverified free-form task (" + (modelDone ? "model said done" : "model did not finish") + ")";
            }

            yield return bridge.EndEpisode(new EpisodeEndRequest
            {
                episode_id = episodeId,
                success = success,
                steps = steps,
                reason = endReason + " | " + why,
                last_result = last,
            }, _ => { }, e => Debug.LogWarning("AgentLoop: episode/end failed: " + e));

            outcome.success = success;
            outcome.steps = steps;
            outcome.reason = endReason + " | " + why;
            outcome.infrastructureError = endReason.StartsWith("bridge error") || endReason.StartsWith("model backend");
            AddLog($"END: {endReason}");
            AddLog($"{(success ? "SUCCESS" : "FAILED")}: {why}");
            Finish($"{(success ? "SUCCESS" : "FAILED")} after {steps} steps - {why}", keyboard, keyboardWasEnabled);
            Debug.Log($"[Agent] {(success ? "SUCCESS" : "FAILED")} \"{task}\" in {steps} steps. {endReason}. {why}");
        }

        void Finish(string message, KeyboardDebug keyboard, bool keyboardWasEnabled)
        {
            status = message;
            running = false;
            if (keyboard != null) keyboard.enabled = keyboardWasEnabled;
            if (outcome != null && string.IsNullOrEmpty(outcome.reason)) outcome.reason = message;
            var cb = onDone; onDone = null;
            cb?.Invoke(outcome);
        }

        public void ResetRoom()
        {
            if (Busy || (robot != null && robot.Busy)) return;
            if (sceneReset != null && sceneReset.HasSnapshot) { sceneReset.Restore(); status = "Room reset."; }
            else KeyboardDebug.ReloadScene();
        }

        public void AddLog(string line)
        {
            log.Add(line);
            if (log.Count > 200) log.RemoveAt(0);
            LogVersion++;
        }

        public void SelectPreset(int delta)
        {
            if (tasks == null || tasks.tasks.Length == 0) return;
            presetIndex = (presetIndex + delta + tasks.tasks.Length) % tasks.tasks.Length;
            taskText = CurrentPreset.task;
            taskEdited = false;
        }
    }
}
