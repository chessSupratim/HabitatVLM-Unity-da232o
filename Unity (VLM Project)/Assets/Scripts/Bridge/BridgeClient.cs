// BridgeClient.cs - HTTP calls to the Python bridge (SPEC section 7).
// Attach to the "AgentManager" object. All calls are coroutines on the main thread.
//
// Example:
//   yield return bridge.Health(h => Debug.Log(h.backend), err => Debug.LogError(err));
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace HabitatVLM
{
    public class BridgeClient : MonoBehaviour
    {
        [Tooltip("Use 127.0.0.1, not localhost: on Windows 'localhost' can add a 2 s IPv6 delay.")]
        public string baseUrl = "http://127.0.0.1:8000";

        [Tooltip("Seconds to wait for /step. Remote models can be slow on the first call.")]
        public int stepTimeoutSeconds = 180;

        public int shortTimeoutSeconds = 10;

        public IEnumerator Health(Action<HealthResponse> ok, Action<string> fail) =>
            Send<HealthResponse>("GET", "/health", null, shortTimeoutSeconds, ok, fail);

        public IEnumerator StartEpisode(EpisodeStartRequest req, Action<EpisodeStartResponse> ok, Action<string> fail) =>
            Send("POST", "/episode/start", JsonUtility.ToJson(req), shortTimeoutSeconds, ok, fail);

        public IEnumerator Step(StepRequest req, Action<StepResponse> ok, Action<string> fail) =>
            Send("POST", "/step", JsonUtility.ToJson(req), stepTimeoutSeconds, ok, fail);

        public IEnumerator EndEpisode(EpisodeEndRequest req, Action<EpisodeEndResponse> ok, Action<string> fail) =>
            Send("POST", "/episode/end", JsonUtility.ToJson(req), shortTimeoutSeconds, ok, fail);

        IEnumerator Send<TResp>(string method, string path, string json, int timeout,
                                Action<TResp> ok, Action<string> fail)
        {
            using (var req = new UnityWebRequest(baseUrl.TrimEnd('/') + path, method))
            {
                if (json != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = timeout;

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string body = req.downloadHandler != null ? req.downloadHandler.text : "";
                    fail?.Invoke($"{method} {path} failed: {req.error} {body}");
                    yield break;
                }

                TResp resp;
                try { resp = JsonUtility.FromJson<TResp>(req.downloadHandler.text); }
                catch (Exception e)
                {
                    fail?.Invoke($"{method} {path}: could not parse reply ({e.Message}): {req.downloadHandler.text}");
                    yield break;
                }
                ok?.Invoke(resp);
            }
        }
    }
}
