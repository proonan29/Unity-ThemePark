using UnityEngine;

namespace ThemePark
{
    public enum ViewMode { Guest, Control }

    /// <summary>Glue between simulation, views, route guide and UI: view mode, start point / visitor, destination, routes.</summary>
    public class AppController : MonoBehaviour
    {
        public SimRunner runner;
        public VisitorRenderer visitorsView;
        public OverlayRenderer overlay;
        public RouteView routeView;
        public CameraRig cameraRig;
        public Transform parkRoot;
        public RideAnimator[] animators;
        public ViewMode view = ViewMode.Guest;
        public bool autoStart = true;

        // guide state
        public Vector2 startPoint;
        public int startVisitor = -1;
        public int destPoi = -1;
        public bool showAlternative = true;
        public GuideResult fastest, shortest;
        public float guideRefreshSeconds = 0.5f;

        public event System.Action GuideChanged;
        public event System.Action ViewChanged;
        public event System.Action SimReset;

        float nextGuide;
        public ParkLayout Layout => runner != null ? runner.EnsureLayout() : null;
        public ParkSim Sim => runner != null ? runner.Sim : null;

        void Awake()
        {
            if (runner != null) runner.SimReset += OnSimReset;
            var L = Layout;
            if (L != null) startPoint = L.nodes[L.gateNode].pos + new Vector2(0f, 6f);
        }

        void Start()
        {
            if (cameraRig != null)
            {
                cameraRig.Clicked += OnClicked;
                cameraRig.followTarget = FollowTarget;
                if (Layout != null) cameraRig.parkBounds = Layout.bounds;
            }
            if (Application.isPlaying)
            {
                if (runner.Sim == null) runner.ResetSim();
                if (autoStart) runner.Play();
                SetView(view);
            }
        }

        void OnDestroy() { if (runner != null) runner.SimReset -= OnSimReset; }

        void OnSimReset()
        {
            startVisitor = -1;
            if (visitorsView != null) visitorsView.selected = -1;
            if (overlay != null) overlay.ResetResources();
            RefreshGuide();
            SimReset?.Invoke();
        }

        Vector3? FollowTarget()
        {
            var v = SelectedVisitor;
            if (v == null) return null;
            var p = Sim.PositionOf(v);
            return new Vector3(p.x, 0f, p.y);
        }

        public Visitor SelectedVisitor
        {
            get
            {
                var s = Sim;
                if (s == null || startVisitor < 0 || startVisitor >= s.visitors.Count) return null;
                var v = s.visitors[startVisitor];
                return v.Active ? v : null;
            }
        }

        // ------------------------------------------------------------------ view

        public void SetView(ViewMode m)
        {
            view = m;
            if (overlay != null)
            {
                overlay.heat = m == ViewMode.Control ? HeatMode.Live : HeatMode.Off;
                overlay.edges = m == ViewMode.Control ? EdgeMode.Density : EdgeMode.Off;
                overlay.Refresh(true);
            }
            if (cameraRig != null)
            {
                if (m == ViewMode.Control) cameraRig.SetMode(CameraRig.Mode.Map);
                else cameraRig.FrameOverview();
            }
            ViewChanged?.Invoke();
        }

        // ------------------------------------------------------------------ guide

        public void SetStartPoint(Vector2 p)
        {
            startVisitor = -1;
            if (visitorsView != null) visitorsView.selected = -1;
            startPoint = p;
            if (cameraRig != null && cameraRig.mode == CameraRig.Mode.Follow) cameraRig.SetMode(CameraRig.Mode.Orbit);
            RefreshGuide();
        }

        public void SetStartGate()
        {
            var L = Layout;
            SetStartPoint(L.nodes[L.gateNode].pos + new Vector2(0f, 6f));
        }

        public bool SetStartVisitor(int id)
        {
            var s = Sim;
            if (s == null || id < 0 || id >= s.visitors.Count || !s.visitors[id].Active) return false;
            startVisitor = id;
            if (visitorsView != null) visitorsView.selected = id;
            RefreshGuide();
            return true;
        }

        /// <summary>Pick a random visitor who is currently walking (for demos / CLI).</summary>
        public int PickWalkingVisitor(int seed = 0)
        {
            var s = Sim; if (s == null) return -1;
            var rng = new System.Random(seed + (int)s.t);
            for (int tries = 0; tries < 200 && s.visitors.Count > 0; tries++)
            {
                var v = s.visitors[rng.Next(s.visitors.Count)];
                if (v.state == VState.Walking && v.goal != Goal.Exit) return v.id;
            }
            return -1;
        }

        public void SetDestination(int poi)
        {
            destPoi = poi;
            RefreshGuide();
        }

        public void ClearGuide()
        {
            destPoi = -1; startVisitor = -1;
            if (visitorsView != null) visitorsView.selected = -1;
            fastest = shortest = null;
            if (routeView != null) routeView.Clear();
            GuideChanged?.Invoke();
        }

        public Vector2 CurrentStart
        {
            get
            {
                var v = SelectedVisitor;
                return v != null ? Sim.PositionOf(v) : startPoint;
            }
        }

        public float StartSpeed { get { var v = SelectedVisitor; return v != null ? v.v0 : Walk.FreeSpeed; } }

        public void RefreshGuide()
        {
            var L = Layout;
            if (L == null) return;
            if (startVisitor >= 0 && SelectedVisitor == null) { startVisitor = -1; if (visitorsView != null) visitorsView.selected = -1; }
            var from = CurrentStart;
            if (destPoi >= 0)
            {
                fastest = RouteGuide.Query(L, Sim, from, destPoi, RouteMode.Fastest, StartSpeed);
                shortest = RouteGuide.Query(L, Sim, from, destPoi, RouteMode.Shortest, StartSpeed);
            }
            else fastest = shortest = null;
            if (routeView != null)
            {
                bool same = fastest != null && shortest != null && SameRoute(fastest, shortest);
                routeView.Set(fastest, showAlternative && !same ? shortest : null, from, destPoi >= 0 ? L.pois[destPoi] : null);
            }
            GuideChanged?.Invoke();
        }

        public static bool SameRoute(GuideResult a, GuideResult b)
        {
            if (a.edges.Count != b.edges.Count) return false;
            for (int i = 0; i < a.edges.Count; i++) if (a.edges[i] != b.edges[i]) return false;
            return true;
        }

        void OnClicked(Vector2 screen)
        {
            if (cameraRig == null || !cameraRig.ScreenToGround(screen, out var p)) return;
            var L = Layout;
            // 1) a visitor under the cursor → follow that visitor
            var s = Sim;
            if (s != null)
            {
                float pick = cameraRig.mode == CameraRig.Mode.Map ? Mathf.Max(1.5f, cameraRig.mapSize * 0.012f) : 1.3f;
                var v = s.NearestVisitor(p, pick);
                if (v != null) { SetStartVisitor(v.id); return; }
            }
            // 2) an attraction → destination
            foreach (var poi in L.pois)
                if (poi.kind != PoiKind.Gate && poi.Contains(p, 1f)) { SetDestination(poi.id); return; }
            // 3) anywhere else → start point
            SetStartPoint(p);
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            AnimateRides();
            if (!Application.isPlaying) return;
            if (Time.unscaledTime >= nextGuide)
            {
                nextGuide = Time.unscaledTime + guideRefreshSeconds;
                if (destPoi >= 0 || startVisitor >= 0) RefreshGuide();
            }
        }

        public void AnimateRides()
        {
            var s = Sim;
            if (animators == null) return;
            foreach (var a in animators)
            {
                if (a == null) continue;
                if (s == null) { a.Apply(Layout.openTime, -1f, 1f, false); continue; }
                var st = s.poi[a.poiId];
                bool open = s.t < s.L.closeTime + 1800f;
                a.Apply(s.t, s.t - st.lastDispatch, Mathf.Max(1f, st.p.duration), open);
            }
        }
    }
}
