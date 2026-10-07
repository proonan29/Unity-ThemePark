using UnityEngine;

namespace ThemePark
{
    /// <summary>Owns the <see cref="ParkSim"/> and advances it in play mode at a speed multiplier (fixed dt, frame-rate independent).</summary>
    public class SimRunner : MonoBehaviour
    {
        public string parkName = ParkPresets.DefaultName;
        public SimParams parameters = new SimParams();
        [Range(1f, 600f)] public float speed = 30f;
        public bool running;
        public int maxStepsPerFrame = 2400;

        public ParkLayout Layout { get; private set; }
        public ParkSim Sim { get; private set; }
        public float LastStepMs { get; private set; }
        public event System.Action SimReset;

        float acc;

        public ParkLayout EnsureLayout()
        {
            if (Layout == null) Layout = ParkPresets.Get(parkName);
            return Layout;
        }

        public ParkSim ResetSim(SimParams p = null)
        {
            if (p != null) parameters = p.Clone();
            EnsureLayout();
            Sim = new ParkSim(Layout, parameters);
            acc = 0f;
            SimReset?.Invoke();
            return Sim;
        }

        public ParkSim EnsureSim() => Sim ?? ResetSim();

        public void Play() { EnsureSim(); running = true; }
        public void Pause() { running = false; }

        /// <summary>Advance <paramref name="seconds"/> of park time immediately (CLI / UI fast-forward).</summary>
        public void Advance(float seconds)
        {
            EnsureSim();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float end = Sim.t + seconds;
            while (Sim.t < end && !Done) Sim.Step();
            LastStepMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        public bool Done => Sim != null && Sim.Finished && Sim.t > Sim.L.closeTime + 60f;

        void Update()
        {
            if (Application.isPlaying && Sim == null) ResetSim();   // e.g. after a script reload in play mode
            if (!Application.isPlaying || !running || Sim == null) return;
            if (Done) { running = false; return; }
            acc += Time.unscaledDeltaTime * speed;
            float dt = Sim.P.dt;
            int n = Mathf.Min(Mathf.FloorToInt(acc / dt), maxStepsPerFrame);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) Sim.Step();
            acc -= n * dt;
            if (acc > dt * maxStepsPerFrame) acc = 0f;   // cannot keep up: drop the backlog
            if (n > 0) LastStepMs = (float)sw.Elapsed.TotalMilliseconds;
        }
    }
}
