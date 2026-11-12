// TaskRunner.cs - run the whole task file automatically for evaluation (PLAN Phase 8).
// Attach to "AgentManager" next to AgentLoop and SceneReset. In Play mode use the panel's
// "Suite" row: choose a tier (all/easy/medium/hard, or "compare" = the tasks marked
// "compare": true - a small set for quota-limited backends like Gemini free tier), repeats (x1..x10), then "Run all".
//
// For each task: reset the room -> run the episode -> next. Results go to the bridge logs
// (episodes.csv has task_id, backend, success, steps, latency) for eval/analyze.py.
// The suite stops early if the bridge or model becomes unreachable.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HabitatVLM
{
    [RequireComponent(typeof(AgentLoop))]
    public class TaskRunner : MonoBehaviour
    {
        public string tierFilter = "all";
        [Range(1, 10)] public int repeats = 1;
        public float pauseBetweenTasks = 1.0f;

        public bool Running { get; private set; }
        public string Progress { get; private set; } = "";

        AgentLoop agent;
        SceneReset reset;
        bool stopRequested;

        void Awake()
        {
            agent = GetComponent<AgentLoop>();
            reset = GetComponent<SceneReset>();
        }

        public void RequestStop() => stopRequested = true;

        public void StartSuite()
        {
            if (Running || agent.Running) return;
            if (reset == null) { Progress = "Needs a SceneReset component on AgentManager (run the Setup menu)."; return; }
            StartCoroutine(RunSuite());
        }

        IEnumerator RunSuite()
        {
            var all = agent.Tasks?.tasks ?? new TaskSpec[0];
            var list = all.Where(t => tierFilter == "all" || t.tier == tierFilter
                                      || (tierFilter == "compare" && t.compare)).ToList();
            if (list.Count == 0)
            {
                Progress = tierFilter == "compare" ? "No tasks marked \"compare\" in this room's task file."
                                                   : $"No tasks for tier '{tierFilter}'.";
                yield break;
            }

            Running = true;
            stopRequested = false;
            var results = new List<(TaskSpec spec, EpisodeOutcome outcome)>();
            int total = list.Count * repeats, n = 0;
            float t0 = Time.realtimeSinceStartup;

            for (int r = 0; r < repeats && !stopRequested; r++)
            {
                foreach (var spec in list)
                {
                    if (stopRequested) break;
                    n++;
                    Progress = $"{n}/{total}  {spec.id} [{spec.tier}]  (passed so far: {results.Count(x => x.outcome.success)}/{results.Count})";
                    reset.Restore();
                    yield return new WaitForSeconds(0.5f);

                    EpisodeOutcome outcome = null;
                    yield return agent.RunEpisode(spec, spec.task, o => outcome = o);
                    outcome ??= new EpisodeOutcome { taskId = spec.id, reason = "no outcome", infrastructureError = true };
                    results.Add((spec, outcome));
                    Debug.Log($"[Suite] {n}/{total} {spec.id} [{spec.tier}] {(outcome.success ? "SUCCESS" : "FAILED")} in {outcome.steps} steps - {outcome.reason}");

                    if (outcome.infrastructureError)
                    {
                        Progress = $"Stopped at {spec.id}: bridge/model unreachable ({outcome.reason}). Fix it and run again.";
                        stopRequested = true;
                        break;
                    }
                    yield return new WaitForSeconds(pauseBetweenTasks);
                }
            }

            reset.Restore();
            float minutes = (Time.realtimeSinceStartup - t0) / 60f;
            var byTier = results.GroupBy(x => x.spec.tier)
                                .Select(g => $"{g.Key} {g.Count(x => x.outcome.success)}/{g.Count()}");
            string summary = $"{results.Count(x => x.outcome.success)}/{results.Count} passed  ({string.Join(", ", byTier)})  in {minutes:F1} min";
            if (!Progress.StartsWith("Stopped")) Progress = (stopRequested ? "Stopped by user. " : "Done. ") + summary;
            agent.AddLog("SUITE: " + summary);
            foreach (var (spec, o) in results)
                agent.AddLog($"  {(o.success ? "PASS" : "FAIL")} {spec.id} [{spec.tier}] {o.steps} steps  {spec.task}");
            Debug.Log("[Suite] " + summary);
            Running = false;
        }
    }
}
