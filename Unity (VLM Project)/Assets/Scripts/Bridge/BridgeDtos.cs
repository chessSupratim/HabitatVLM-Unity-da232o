// BridgeDtos.cs - JSON shapes exchanged with the Python bridge (SPEC section 7).
// JsonUtility cannot write null or dictionaries, so:
//   - "no value" is sent as an empty string ""   (the bridge treats "" as null)
//   - dictionaries are sent as arrays of {name, state} / {name, kind}
using System;

namespace HabitatVLM
{
    [Serializable] public class NameState { public string name; public string state; }
    [Serializable] public class NameKind  { public string name; public string kind; }

    [Serializable]
    public class HealthResponse { public string status; public string backend; public string model; }

    [Serializable]
    public class EpisodeStartRequest
    {
        public string task;
        public string[] scene_objects;
        public NameKind[] object_kinds = new NameKind[0];
        public int max_steps = 15;
        public string task_id = "";
    }

    [Serializable] public class EpisodeStartResponse { public string episode_id; }

    [Serializable]
    public class RobotStateDto
    {
        public string holding = "";
        public string[] near = new string[0];
        public NameState[] containers = new NameState[0];
        // where every item is: state = "on shelf_a" | "in cabinet_left" | "held by robot"
        public NameState[] items = new NameState[0];
    }

    [Serializable]
    public class ActionResultDto
    {
        public string action = "";
        public string target = "";
        public string @object = "";     // serialised as "object"
        public string location = "";
        public bool success;
        public string reason = "";
    }

    [Serializable]
    public class StepRequest
    {
        public string episode_id;
        public int step;
        public string image_b64;
        public RobotStateDto robot_state = new RobotStateDto();
        public ActionResultDto last_result = new ActionResultDto();
    }

    [Serializable]
    public class StepMeta
    {
        public string backend;
        public string model;
        public int latency_ms;
        public bool valid;
        public int retries;
        public string error;
    }

    [Serializable]
    public class StepResponse
    {
        public string action;
        public string target;
        public string @object;
        public string location;
        public string thought;
        public StepMeta meta;

        public override string ToString()
        {
            string args = "";
            if (!string.IsNullOrEmpty(target))   args += "target=" + target + " ";
            if (!string.IsNullOrEmpty(@object))  args += "object=" + @object + " ";
            if (!string.IsNullOrEmpty(location)) args += "location=" + location;
            return $"{action}({args.Trim()})";
        }
    }

    [Serializable]
    public class EpisodeEndRequest
    {
        public string episode_id;
        public bool success;
        public int steps;
        public string reason = "";
        public ActionResultDto last_result = new ActionResultDto();
    }

    [Serializable] public class EpisodeEndResponse { public bool logged; }
}
