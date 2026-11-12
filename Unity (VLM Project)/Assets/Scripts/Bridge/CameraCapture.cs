// CameraCapture.cs - grabs the robot camera's RenderTexture as JPEG bytes (SPEC section 9).
// Attach to "robot_cam" (the camera must have a Target Texture, e.g. RobotCamRT 512x512).
using System;
using System.Collections;
using UnityEngine;

namespace HabitatVLM
{
    [RequireComponent(typeof(Camera))]
    public class CameraCapture : MonoBehaviour
    {
        [Range(1, 100)] public int jpegQuality = 75;

        Camera cam;
        Texture2D readback;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam.targetTexture == null)
                Debug.LogError("CameraCapture: robot_cam has no Target Texture. Assign RobotCamRT (512x512).");
        }

        public RenderTexture Texture => cam != null ? cam.targetTexture : null;

        /// Waits for the end of the frame (so the camera has rendered), then returns JPEG bytes.
        public IEnumerator Capture(Action<byte[]> done)
        {
            yield return new WaitForEndOfFrame();
            done(CaptureNow());
        }

        /// Reads whatever is currently in the RenderTexture. Prefer Capture() from coroutines.
        public byte[] CaptureNow()
        {
            var rt = cam.targetTexture;
            if (rt == null) return null;
            if (readback == null || readback.width != rt.width || readback.height != rt.height)
                readback = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readback.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readback.Apply(false);
            RenderTexture.active = prev;
            return readback.EncodeToJPG(jpegQuality);
        }

        void OnDestroy()
        {
            if (readback != null) Destroy(readback);
        }
    }
}
