using UnityEngine;

namespace ThemePark
{
    /// <summary>Moves the parts of one ride model from the simulation state (time since the last dispatch).</summary>
    public class RideAnimator : MonoBehaviour
    {
        public int poiId;
        public RideModel model;
        public Transform rotor;          // carousel / teacups / swing / ferris wheel
        public Transform lift;           // drop tower gondola
        public Transform pendulum;       // pirate ship
        public Transform[] movers;       // train cars, boats, bumper cars
        public Transform[] subs;         // teacups, swing chains, gondolas, horses
        public float[] subBaseY;
        public float[] subBaseYaw;
        public Vector3[] track;          // closed local polyline
        public float spacing = 2.2f;
        public Vector2 extent;           // bumper car rink half size
        public float towerTop = 40f;

        float[] cum;
        float total;

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void EnsureTrack()
        {
            if (track == null || track.Length < 2) return;
            if (cum != null && cum.Length == track.Length + 1) return;
            cum = new float[track.Length + 1];
            for (int i = 0; i < track.Length; i++) cum[i + 1] = cum[i] + Vector3.Distance(track[i], track[(i + 1) % track.Length]);
            total = cum[track.Length];
        }

        /// <summary>Point and tangent at arc length <paramref name="d"/> (wraps around).</summary>
        public Vector3 AtDistance(float d, out Vector3 tangent)
        {
            EnsureTrack();
            tangent = Vector3.forward;
            if (cum == null || total <= 0f) return Vector3.zero;
            d = Mathf.Repeat(d, total);
            int lo = 0, hi = track.Length;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (cum[m] <= d) lo = m; else hi = m; }
            var a = track[lo]; var b = track[(lo + 1) % track.Length];
            float seg = Mathf.Max(cum[lo + 1] - cum[lo], 1e-5f);
            tangent = (b - a) / seg;
            return Vector3.Lerp(a, b, (d - cum[lo]) / seg);
        }

        public float TrackLength { get { EnsureTrack(); return total; } }

        void PlaceTrain(float headDistance)
        {
            if (movers == null) return;
            for (int k = 0; k < movers.Length; k++)
            {
                if (movers[k] == null) continue;
                var p = AtDistance(headDistance - k * spacing, out var tan);
                movers[k].localPosition = p;
                if (tan.sqrMagnitude > 1e-6f) movers[k].localRotation = Quaternion.LookRotation(tan, Vector3.up);
            }
        }

        /// <param name="simTime">clock (s)</param>
        /// <param name="since">seconds since the last dispatch (negative or large = idle)</param>
        /// <param name="duration">ride duration (s)</param>
        /// <param name="open">park operating (continuous rides keep moving)</param>
        public void Apply(float simTime, float since, float duration, bool open)
        {
            bool running = since >= 0f && since < duration;
            float ph = running ? since / duration : 0f;
            switch (model)
            {
                case RideModel.Coaster:
                {
                    float s = ph < 0.35f ? 0.25f * ph / 0.35f : 0.25f + 0.75f * Smooth((ph - 0.35f) / 0.65f * 1.15f - 0.08f);
                    PlaceTrain((running ? s : 0f) * TrackLength + (movers != null ? movers.Length * spacing * 0.5f : 0f));
                    break;
                }
                case RideModel.MiniCoaster:
                    PlaceTrain((running ? Smooth(ph) * 2f : 0f) * TrackLength + 3f);
                    break;
                case RideModel.KiddieTrain:
                    PlaceTrain((running ? Smooth(ph) * 2f : 0f) * TrackLength + 3f);
                    break;
                case RideModel.LogFlume:
                    if (movers != null)
                        for (int k = 0; k < movers.Length; k++)
                        {
                            float f = open ? Mathf.Repeat(simTime / duration + (float)k / movers.Length, 1f) : (float)k / movers.Length;
                            var p = AtDistance(f * TrackLength, out var tan);
                            movers[k].localPosition = p;
                            if (tan.sqrMagnitude > 1e-6f) movers[k].localRotation = Quaternion.LookRotation(tan, Vector3.up);
                        }
                    break;
                case RideModel.FerrisWheel:
                    if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, 0f, (open ? simTime / duration : 0f) * 360f);
                    if (subs != null) foreach (var g in subs) if (g != null) g.rotation = transform.rotation;
                    break;
                case RideModel.Carousel:
                {
                    float ang = 360f * (duration / 20f) * Smooth(ph);
                    if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, ang, 0f);
                    if (subs != null)
                        for (int k = 0; k < subs.Length; k++)
                        {
                            var lp = subs[k].localPosition;
                            lp.y = subBaseY[k] + (running ? 0.35f * Mathf.Sin(ang * Mathf.Deg2Rad * 3f + k * 1.7f) : 0f);
                            subs[k].localPosition = lp;
                        }
                    break;
                }
                case RideModel.Teacups:
                {
                    float ang = 360f * (duration / 15f) * Smooth(ph);
                    if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, ang, 0f);
                    if (subs != null)
                        for (int k = 0; k < subs.Length; k++)
                            subs[k].localRotation = Quaternion.Euler(0f, (k % 2 == 0 ? -2.5f : 3.2f) * ang, 0f);
                    break;
                }
                case RideModel.Swing:
                {
                    float ang = 360f * (duration / 9f) * Smooth(ph);
                    float tilt = running ? 42f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(ph * 1.05f))), 0.6f) : 0f;
                    if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, ang, 0f);
                    if (subs != null)
                        for (int k = 0; k < subs.Length; k++)
                            subs[k].localRotation = Quaternion.Euler(0f, subBaseYaw[k], 0f) * Quaternion.Euler(0f, 0f, tilt);
                    break;
                }
                case RideModel.DropTower:
                    if (lift != null)
                    {
                        float y = 1.2f;
                        if (running)
                        {
                            if (ph < 0.55f) y = Mathf.Lerp(1.2f, towerTop, Smooth(ph / 0.55f));
                            else if (ph < 0.70f) y = towerTop;
                            else if (ph < 0.78f) { float q = (ph - 0.70f) / 0.08f; y = Mathf.Lerp(towerTop, 7f, q * q); }
                            else y = Mathf.Lerp(7f, 1.2f, Smooth((ph - 0.78f) / 0.22f));
                        }
                        var lp = lift.localPosition; lp.y = y; lift.localPosition = lp;
                    }
                    break;
                case RideModel.PirateShip:
                    if (pendulum != null)
                    {
                        float env = running ? Smooth(ph / 0.3f) * (1f - Smooth((ph - 0.72f) / 0.28f)) : 0f;
                        float a = 72f * env * Mathf.Sin(2f * Mathf.PI * since / 8f);
                        pendulum.localRotation = Quaternion.Euler(0f, 0f, a);
                    }
                    break;
                case RideModel.BumperCars:
                    if (movers != null)
                        for (int k = 0; k < movers.Length; k++)
                        {
                            Vector3 p; Vector3 d;
                            if (running && ph > 0.05f && ph < 0.95f)
                            {
                                float tt = since * 0.35f;
                                float fa = 0.9f + 0.13f * k, fb = 0.7f + 0.17f * (k % 5), pa = k * 1.3f, pb = k * 2.1f;
                                p = new Vector3(extent.x * Mathf.Sin(fa * tt + pa), 0f, extent.y * Mathf.Sin(fb * tt + pb));
                                d = new Vector3(extent.x * fa * Mathf.Cos(fa * tt + pa), 0f, extent.y * fb * Mathf.Cos(fb * tt + pb));
                            }
                            else
                            {
                                int cols = Mathf.Max(1, movers.Length / 2);
                                p = new Vector3(-extent.x + (k % cols) * (2f * extent.x / Mathf.Max(1, cols - 1)), 0f, (k < cols ? -0.6f : 0.6f) * extent.y);
                                d = Vector3.forward;
                            }
                            movers[k].localPosition = p;
                            if (d.sqrMagnitude > 1e-4f) movers[k].localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                        }
                    break;
            }
        }
    }
}
