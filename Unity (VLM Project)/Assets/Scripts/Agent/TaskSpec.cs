// TaskSpec.cs - tasks and their success conditions (SPEC 11). Unity, not the VLM, decides success.
//
// Task file (one per room), e.g. Assets/Tasks/tasks_room01.json:
// { "room": "room_01",
//   "tasks": [ { "id": "T01", "tier": "easy", "task": "Open the left cabinet",
//                "success": { "type": "container_state", "target": "cabinet_left", "state": "open" } } ] }
//
// Condition types:
//   item_in / item_on   object is in/on location (and not held)
//   container_state     target is "open" or "closed"
//   holding             robot holds object
//   all                 every entry in "conditions" is true
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HabitatVLM
{
    [Serializable]
    public class Condition
    {
        public string type;
        public string @object;
        public string location;
        public string target;
        public string state;
    }

    [Serializable]
    public class SuccessSpec : Condition
    {
        public Condition[] conditions;
    }

    [Serializable]
    public class TaskSpec
    {
        public string id;
        public string tier;
        public bool compare;            // in the small backend-comparison set ("compare" suite)
        public string task;
        public SuccessSpec success;

        public bool HasCondition => success != null && !string.IsNullOrEmpty(success.type);
    }

    [Serializable]
    public class TaskFile
    {
        public string room;
        public TaskSpec[] tasks;

        public static TaskFile Parse(TextAsset asset)
        {
            if (asset == null) return new TaskFile { tasks = new TaskSpec[0] };
            var f = JsonUtility.FromJson<TaskFile>(asset.text);
            f.tasks ??= new TaskSpec[0];
            return f;
        }
    }

    public static class SuccessChecker
    {
        /// Returns true/false and a human-readable reason.
        public static bool Check(SuccessSpec spec, RobotSkills robot, out string reason)
        {
            if (spec == null || string.IsNullOrEmpty(spec.type)) { reason = "no success condition"; return false; }
            if (spec.type == "all")
            {
                var parts = new List<string>();
                bool all = true;
                foreach (var c in spec.conditions ?? new Condition[0])
                {
                    bool ok = CheckOne(c, robot, out var r);
                    parts.Add((ok ? "OK " : "NO ") + r);
                    all &= ok;
                }
                reason = string.Join("; ", parts);
                return all;
            }
            return CheckOne(spec, robot, out reason);
        }

        static bool CheckOne(Condition c, RobotSkills robot, out string reason)
        {
            switch (c.type)
            {
                case "item_in":
                case "item_on":
                {
                    var item = Interactable.Find(c.@object);
                    var loc = Interactable.Find(c.location);
                    if (item == null || loc == null) { reason = $"unknown object in condition ({c.@object}, {c.location})"; return false; }
                    bool ok = !item.isHeld && item.location == loc;
                    string where = item.isHeld ? "held by robot" : item.location != null ? $"at {item.location.Name}" : "nowhere";
                    reason = $"{item.Name} {(c.type == "item_in" ? "in" : "on")} {loc.Name} (actually {where})";
                    return ok;
                }
                case "container_state":
                {
                    var t = Interactable.Find(c.target);
                    if (t == null) { reason = $"unknown object {c.target}"; return false; }
                    string actual = t.IsOpen ? "open" : "closed";
                    reason = $"{t.Name} {c.state} (actually {actual})";
                    return actual == (c.state ?? "").ToLowerInvariant();
                }
                case "holding":
                {
                    string held = robot.Holding != null ? robot.Holding.Name : "nothing";
                    reason = $"holding {c.@object} (actually {held})";
                    return held == c.@object;
                }
                default:
                    reason = $"unknown condition type '{c.type}'";
                    return false;
            }
        }
    }
}
