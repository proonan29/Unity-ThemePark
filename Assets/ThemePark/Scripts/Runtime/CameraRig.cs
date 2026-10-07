using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ThemePark
{
    /// <summary>Orbit camera (left-drag rotate, right/middle-drag pan, wheel zoom), top-down map mode (orthographic,
    /// drag to pan) and follow mode. Reports clicks (press + release without dragging) to the app.</summary>
    public class CameraRig : MonoBehaviour
    {
        public enum Mode { Orbit, Map, Follow }
        public Mode mode = Mode.Orbit;
        public Vector3 pivot = Vector3.zero;
        public float yaw = 20f, pitch = 48f, distance = 260f;
        public float followDistance = 40f;
        public float mapSize = 140f;
        public Rect parkBounds = new Rect(-142f, -134f, 284f, 276f);
        [Range(0f, 0.5f)] public float viewportLeft;     // fraction of the screen covered by the left panel
        [Range(0f, 0.5f)] public float viewportRight;    // fraction covered by the right dashboard
        public System.Func<Vector2, bool> isOverUI;
        public System.Func<Vector3?> followTarget;
        public event System.Action<Vector2> Clicked;

        Camera cam;
        Vector3 followPivot;
        bool dragging, pressed;
        Vector2 pressPos;
        float pressTime;

        public Camera Cam { get { if (cam == null) cam = GetComponent<Camera>(); return cam; } }

        public void FrameOverview()
        {
            mode = Mode.Orbit;
            pivot = new Vector3(parkBounds.center.x, 0f, parkBounds.center.y - 22f);
            yaw = 12f; pitch = 52f;
            distance = Mathf.Max(parkBounds.width, parkBounds.height) * 1.05f;
            Place();
        }

        public void FrameMap()
        {
            mode = Mode.Map;
            pivot = new Vector3(parkBounds.center.x, 0f, parkBounds.center.y);
            FitMap();
            Place();
        }

        void FitMap()
        {
            float free = Mathf.Max(0.2f, 1f - viewportLeft - viewportRight);
            float aspect = Mathf.Max(0.5f, Cam.aspect);
            mapSize = Mathf.Max(parkBounds.height * 0.5f, parkBounds.width * 0.5f / (aspect * free)) * 1.03f;
        }

        public void SetMode(Mode m)
        {
            if (m == Mode.Map) { FrameMap(); return; }
            if (m == Mode.Orbit && mode == Mode.Map) { FrameOverview(); return; }
            mode = m;
            if (m == Mode.Follow && followTarget != null) { var p = followTarget(); if (p.HasValue) followPivot = p.Value; }
            Place();
        }

        public void LookAt(Vector3 p, float dist = 70f)
        {
            if (mode == Mode.Map) { pivot = p; }
            else { mode = Mode.Orbit; pivot = p; distance = dist; }
            Place();
        }

        void LateUpdate()
        {
            HandleInput();
            Place();
        }

        void HandleInput()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            bool over = isOverUI != null && isOverUI(pos);
            bool anyPress = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            if (anyPress) dragging = !over;
            if (mouse.leftButton.wasPressedThisFrame && !over) { pressed = true; pressPos = pos; pressTime = Time.unscaledTime; }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (pressed && (pos - pressPos).magnitude < 6f && Time.unscaledTime - pressTime < 0.6f) Clicked?.Invoke(pos);
                pressed = false;
            }
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed) dragging = false;
            Vector2 d = mouse.delta.ReadValue();
            if (pressed && (pos - pressPos).magnitude < 6f) d = Vector2.zero;   // ignore jitter before deciding click vs drag
            if (mode == Mode.Map)
            {
                if (dragging && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed))
                {
                    float k = 2f * mapSize / Mathf.Max(1f, Screen.height);
                    pivot += new Vector3(-d.x * k, 0f, -d.y * k);
                }
            }
            else if (dragging && mouse.leftButton.isPressed)
            {
                yaw += d.x * 0.25f;
                pitch = Mathf.Clamp(pitch - d.y * 0.25f, 8f, 89f);
            }
            else if (dragging && (mouse.rightButton.isPressed || mouse.middleButton.isPressed))
            {
                float k = (mode == Mode.Follow ? followDistance : distance) * 0.0015f;
                Vector3 right = transform.right, fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                if (mode == Mode.Follow) { mode = Mode.Orbit; pivot = followPivot; distance = followDistance; }
                pivot += (-right * d.x - fwd * d.y) * k;
            }
            float wheel = mouse.scroll.ReadValue().y;
            if (!over && Mathf.Abs(wheel) > 0.01f)
            {
                float f = Mathf.Pow(0.9f, Mathf.Sign(wheel) * Mathf.Min(3f, Mathf.Abs(wheel) / 120f + 0.5f));
                if (mode == Mode.Map) mapSize = Mathf.Clamp(mapSize * f, 10f, 260f);
                else if (mode == Mode.Follow) followDistance = Mathf.Clamp(followDistance * f, 4f, 200f);
                else distance = Mathf.Clamp(distance * f, 6f, 600f);
            }
#endif
        }

        public void Place()
        {
            var c = Cam;
            if (mode == Mode.Map)
            {
                transform.position = pivot + Vector3.up * 300f;
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                if (c != null)
                {
                    c.orthographic = true;
                    c.orthographicSize = mapSize;
                    c.ResetProjectionMatrix();
                    float shift = viewportLeft - viewportRight;
                    if (Mathf.Abs(shift) > 0.001f) { var m = c.projectionMatrix; m[0, 3] = shift; c.projectionMatrix = m; }
                }
                return;
            }
            Vector3 p = pivot; float dist = distance;
            if (mode == Mode.Follow && followTarget != null)
            {
                var ft = followTarget();
                if (ft.HasValue) followPivot = Vector3.Lerp(followPivot, ft.Value, Application.isPlaying ? 1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f) : 1f);
                p = followPivot + Vector3.up * 1f; dist = followDistance;
            }
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = p - rot * Vector3.forward * dist;
            transform.rotation = rot;
            if (c != null)
            {
                c.orthographic = false;
                c.ResetProjectionMatrix();
                float shift = viewportLeft - viewportRight;
                if (Mathf.Abs(shift) > 0.001f) { var m = c.projectionMatrix; m[0, 2] = -shift; c.projectionMatrix = m; }
            }
        }

        /// <summary>Ground point (y = 0) under a screen position.</summary>
        public bool ScreenToGround(Vector2 screen, out Vector2 xz)
        {
            xz = default;
            var c = Cam;
            if (c == null) return false;
            var ray = c.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            var p = ray.origin + ray.direction * t;
            xz = new Vector2(p.x, p.z);
            return true;
        }

        public bool WorldToScreen(Vector3 world, out Vector2 screen)
        {
            var c = Cam;
            screen = default;
            if (c == null) return false;
            var s = c.WorldToScreenPoint(world);
            screen = new Vector2(s.x, s.y);
            return s.z > 0f;
        }
    }
}
