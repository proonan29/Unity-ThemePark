using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ThemePark.UI
{
    /// <summary>UI Toolkit front end built in code: left control panel (simulation, route guide, display), floating ride labels,
    /// and the control-room dashboard on the right.</summary>
    [RequireComponent(typeof(UIDocument))]
    public class ThemeParkUI : MonoBehaviour
    {
        public AppController app;

        public static readonly Color Bg = new Color(0.07f, 0.08f, 0.10f, 0.93f), Card = new Color(0.13f, 0.14f, 0.17f, 1f),
            Accent = new Color(0.20f, 0.78f, 0.95f), Text = new Color(0.92f, 0.93f, 0.95f), Dim = new Color(0.62f, 0.65f, 0.70f),
            Btn = new Color(0.21f, 0.23f, 0.27f), BtnOn = new Color(0.13f, 0.55f, 0.78f), Good = new Color(0.35f, 0.85f, 0.45f),
            Warn = new Color(1f, 0.70f, 0.20f), Bad = new Color(1f, 0.35f, 0.30f);

        public static readonly int[] VisitorChoices = { 3000, 5000, 8000, 12000, 15000 };
        public static readonly float[] GuideChoices = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        VisualElement root, panel, labelLayer;
        Dashboard dashboard;
        Label clock, status, simInfo, speedLabel, startInfo, guideOut, legend, hint, applyNote;
        VisualElement dayFill;
        Button startBtn, viewGuest, viewControl, labelsBtn;
        Slider speedSlider;
        DropdownField destDrop, visitorsDrop, guideDrop;
        readonly Dictionary<ColorBy, Button> colorBtns = new Dictionary<ColorBy, Button>();
        readonly Dictionary<HeatMode, Button> heatBtns = new Dictionary<HeatMode, Button>();
        readonly Dictionary<EdgeMode, Button> edgeBtns = new Dictionary<EdgeMode, Button>();
        Button langKo, langEn;
        readonly List<int> destIds = new List<int>();
        readonly List<(Poi p, Label l, float h)> poiLabels = new List<(Poi, Label, float)>();
        bool showLabels = true;
        float nextRefresh, nextLabelText;
        bool built;
        float lastLeft = -1f, lastRight = -1f;

        static string F0(float v) => v.ToString("0", CultureInfo.InvariantCulture);
        static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        public static string Clock(float t) { int s = Mathf.FloorToInt(t); return $"{s / 3600:00}:{s / 60 % 60:00}"; }
        public static string Dur(float s)
        {
            if (s < 60f) return Loc.F("{0} s", F0(s));
            int m = Mathf.RoundToInt(s / 60f);
            return m >= 60 ? Loc.F("{0} h {1} min", m / 60, m % 60) : Loc.F("{0} min", m);
        }
        public static string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);

        void OnEnable()
        {
            Loc.Changed += Rebuild;
            Build();
        }

        void OnDisable() { Loc.Changed -= Rebuild; }

        void Start()
        {
            if (app != null)
            {
                app.GuideChanged += RefreshGuideText;
                app.ViewChanged += OnViewChanged;
                app.SimReset += RefreshAll;
                if (app.cameraRig != null) app.cameraRig.isOverUI = IsOverPanel;
            }
            Rebuild();
        }

        void Rebuild() { Build(); RefreshAll(); }

        void RefreshAll() { RefreshButtons(); RefreshGuideText(); Refresh(); }

        public bool IsOverPanel(Vector2 screenPos)
        {
            if (panel == null || panel.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel.panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            if (panel.worldBound.Contains(p)) return true;
            return dashboard != null && dashboard.Visible && dashboard.worldBound.Contains(p);
        }

        // ---------- construction helpers ----------

        public static void Pad(VisualElement e, float v) { e.style.paddingLeft = e.style.paddingRight = e.style.paddingTop = e.style.paddingBottom = v; }
        public static void Radius(VisualElement e, float r) { e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r; }

        public static Label L(string text, float size = 13, Color? c = null, bool bold = false)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = c ?? Text;
            l.style.whiteSpace = WhiteSpace.Normal;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 2;
            l.enableRichText = true;
            return l;
        }

        public static Button B(string text, System.Action onClick, float grow = 1f)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow = grow;
            b.style.height = 28;
            b.style.fontSize = 12;
            b.style.color = Text;
            b.style.backgroundColor = Btn;
            b.style.borderLeftWidth = b.style.borderRightWidth = b.style.borderTopWidth = b.style.borderBottomWidth = 0;
            Radius(b, 5);
            b.style.marginLeft = b.style.marginRight = 2;
            return b;
        }

        public static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.marginBottom = 4;
            return r;
        }

        public static VisualElement Section(VisualElement parent, string text)
        {
            var c = new VisualElement();
            c.style.backgroundColor = Card;
            Radius(c, 7);
            Pad(c, 9);
            c.style.marginBottom = 8;
            var header = L(text, 12, Dim, true);
            header.style.marginBottom = 5;
            c.Add(header);
            parent.Add(c);
            return c;
        }

        static DropdownField Drop(List<string> choices, int index, System.Action<int> onChange)
        {
            var d = new DropdownField(choices, Mathf.Clamp(index, 0, Mathf.Max(0, choices.Count - 1)));
            d.style.fontSize = 12;
            d.RegisterValueChangedCallback(e => { int i = choices.IndexOf(e.newValue); if (i >= 0) onChange(i); });
            return d;
        }

        // ---------- build ----------

        void Build()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null || doc.rootVisualElement == null) return;
            root = doc.rootVisualElement;
            root.Clear();
            colorBtns.Clear(); heatBtns.Clear(); edgeBtns.Clear(); poiLabels.Clear(); destIds.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.flexDirection = FlexDirection.Row;

            labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            labelLayer.style.position = Position.Absolute;
            labelLayer.style.left = labelLayer.style.top = labelLayer.style.right = labelLayer.style.bottom = 0;
            root.Add(labelLayer);

            panel = new VisualElement();
            panel.style.width = 380;
            panel.style.backgroundColor = Bg;
            Pad(panel, 10);
            root.Add(panel);
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1;
            root.Add(spacer);
            dashboard = new Dashboard(app);
            root.Add(dashboard);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            panel.Add(scroll);

            scroll.Add(L("ThemePark", 22, Accent, true));
            var sub = L(Loc.T("Theme park flow & congestion simulator"), 13, Dim);
            sub.style.marginBottom = 8;
            scroll.Add(sub);

            // view
            var rv = Row(); rv.style.marginBottom = 8;
            viewGuest = B(Loc.T("Guest view"), () => app?.SetView(ViewMode.Guest));
            viewControl = B(Loc.T("Control room"), () => app?.SetView(ViewMode.Control));
            viewGuest.style.height = viewControl.style.height = 32;
            rv.Add(viewGuest); rv.Add(viewControl);
            scroll.Add(rv);

            // simulation
            var ss = Section(scroll, Loc.T("Simulation"));
            var cr = Row();
            clock = L("09:00", 30, Text, true); clock.style.marginRight = 10;
            status = L("", 12, Dim); status.style.flexGrow = 1; status.style.alignSelf = Align.Center;
            cr.Add(clock); cr.Add(status);
            ss.Add(cr);
            var bar = new VisualElement();
            bar.style.height = 8; bar.style.backgroundColor = Btn; Radius(bar, 4); bar.style.marginBottom = 6;
            dayFill = new VisualElement(); dayFill.style.height = 8; dayFill.style.backgroundColor = Accent; Radius(dayFill, 4);
            bar.Add(dayFill); ss.Add(bar);
            simInfo = L("", 12, Text); ss.Add(simInfo);
            var r1 = Row(); r1.style.marginTop = 4;
            startBtn = B(Loc.T("Start"), OnStartPause); startBtn.style.backgroundColor = BtnOn;
            r1.Add(startBtn);
            r1.Add(B(Loc.T("+1 hour"), () => { app?.runner.Advance(3600f); Refresh(); }));
            r1.Add(B(Loc.T("Reset"), OnReset));
            ss.Add(r1);
            speedLabel = L("", 12, Text, true); ss.Add(speedLabel);
            speedSlider = new Slider(0f, Mathf.Log10(600f));
            speedSlider.RegisterValueChangedCallback(e => SetSpeed(Mathf.Pow(10f, e.newValue)));
            ss.Add(speedSlider);
            var r2 = Row();
            foreach (var x in new[] { 1f, 10f, 60f, 300f, 600f }) { float v = x; r2.Add(B($"{x:0}x", () => SetSpeed(v))); }
            ss.Add(r2);
            var r3 = Row(); r3.style.marginTop = 4;
            var lv = L(Loc.T("Visitors / day"), 11, Dim); lv.style.width = 90; lv.style.alignSelf = Align.Center;
            var vch = new List<string>(); foreach (var v in VisitorChoices) vch.Add(N(v));
            int curV = app != null ? System.Array.IndexOf(VisitorChoices, app.runner.parameters.dailyVisitors) : 2;
            visitorsDrop = Drop(vch, curV < 0 ? 2 : curV, i => { app.runner.parameters.dailyVisitors = VisitorChoices[i]; applyNote.text = Loc.T("Applies when you press Reset."); });
            visitorsDrop.style.flexGrow = 1;
            r3.Add(lv); r3.Add(visitorsDrop); ss.Add(r3);
            var r4 = Row();
            var lg = L(Loc.T("Guide app users"), 11, Dim); lg.style.width = 90; lg.style.alignSelf = Align.Center;
            var gch = new List<string>(); foreach (var g in GuideChoices) gch.Add($"{g * 100f:0}%");
            int curG = app != null ? System.Array.FindIndex(GuideChoices, g => Mathf.Approximately(g, app.runner.parameters.guidedShare)) : 2;
            guideDrop = Drop(gch, curG < 0 ? 2 : curG, i => { app.runner.parameters.guidedShare = GuideChoices[i]; applyNote.text = Loc.T("Applies when you press Reset."); });
            guideDrop.style.flexGrow = 1;
            r4.Add(lg); r4.Add(guideDrop); ss.Add(r4);
            applyNote = L("", 11, Warn); ss.Add(applyNote);

            // route guide
            var sg = Section(scroll, Loc.T("Route guide"));
            startInfo = L("", 12, Text); sg.Add(startInfo);
            var r5 = Row();
            r5.Add(B(Loc.T("Main gate"), () => app?.SetStartGate()));
            r5.Add(B(Loc.T("Random visitor"), () => { if (app != null) { int id = app.PickWalkingVisitor(UnityEngine.Random.Range(0, 99999)); if (id >= 0) app.SetStartVisitor(id); } }));
            r5.Add(B(Loc.T("Follow"), () => app?.cameraRig?.SetMode(CameraRig.Mode.Follow)));
            r5.Add(B(Loc.T("Clear"), () => app?.ClearGuide()));
            sg.Add(r5);
            var dch = new List<string> { Loc.T("— choose a destination —") };
            destIds.Add(-1);
            if (app != null && app.Layout != null)
            {
                foreach (var kind in new[] { PoiKind.Ride, PoiKind.Food, PoiKind.Rest })
                    foreach (var p in app.Layout.pois)
                        if (p.kind == kind) { dch.Add(KindIcon(p) + " " + Loc.T(p.name)); destIds.Add(p.id); }
            }
            destDrop = Drop(dch, 0, i => app?.SetDestination(destIds[i]));
            sg.Add(destDrop);
            guideOut = L("", 12, Text); guideOut.style.marginTop = 6; sg.Add(guideOut);
            hint = L(Loc.T("Click the map to set a start point, click a ride to set the destination, click a visitor to guide that visitor."), 11, Dim);
            sg.Add(hint);

            // display
            var sd = Section(scroll, Loc.T("Display"));
            sd.Add(L(Loc.T("Visitor colour"), 11, Dim));
            var r6 = Row();
            foreach (var (m, t) in new[] { (ColorBy.State, "State"), (ColorBy.Type, "Group"), (ColorBy.Guide, "App user") })
            { var mm = m; var b = B(Loc.T(t), () => { app.visitorsView.colorBy = mm; RefreshButtons(); }); colorBtns[m] = b; r6.Add(b); }
            sd.Add(r6);
            legend = L("", 11, Dim); sd.Add(legend);
            sd.Add(L(Loc.T("Heat map"), 11, Dim));
            var r7 = Row();
            foreach (var (m, t) in new[] { (HeatMode.Off, "Off"), (HeatMode.Live, "Live density"), (HeatMode.Cumulative, "Cumulative") })
            { var mm = m; var b = B(Loc.T(t), () => { app.overlay.heat = mm; app.overlay.Refresh(true); RefreshButtons(); }); heatBtns[m] = b; r7.Add(b); }
            sd.Add(r7);
            sd.Add(L(Loc.T("Walkways"), 11, Dim));
            var r8 = Row();
            foreach (var (m, t) in new[] { (EdgeMode.Off, "Off"), (EdgeMode.Density, "Level of service"), (EdgeMode.Flow, "Flow / h") })
            { var mm = m; var b = B(Loc.T(t), () => { app.overlay.edges = mm; app.overlay.Refresh(true); RefreshButtons(); }); edgeBtns[m] = b; r8.Add(b); }
            sd.Add(r8);
            var r9 = Row();
            labelsBtn = B(Loc.T("Ride labels"), () => { showLabels = !showLabels; RefreshButtons(); });
            r9.Add(labelsBtn);
            r9.Add(B(Loc.T("Overview"), () => { if (app.view == ViewMode.Control) app.cameraRig.FrameMap(); else app.cameraRig.FrameOverview(); }));
            sd.Add(r9);
            sd.Add(L(Loc.T("Left drag: rotate · Right drag: pan · Wheel: zoom"), 11, Dim));

            // language
            var sl = Section(scroll, Loc.T("Language"));
            var r10 = Row();
            langEn = B("English", () => Loc.Korean = false);
            langKo = B("한국어", () => Loc.Korean = true);
            r10.Add(langEn); r10.Add(langKo);
            sl.Add(r10);

            // floating labels
            if (app != null && app.Layout != null)
                foreach (var p in app.Layout.pois)
                {
                    if (p.kind == PoiKind.Gate) continue;
                    var l = new Label { pickingMode = PickingMode.Ignore };
                    l.style.position = Position.Absolute;
                    l.style.fontSize = 11;
                    l.style.color = Color.white;
                    l.style.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.78f);
                    Pad(l, 3); l.style.paddingLeft = l.style.paddingRight = 6;
                    Radius(l, 4);
                    l.style.borderLeftWidth = 3; l.style.borderLeftColor = p.color;
                    l.enableRichText = true;
                    labelLayer.Add(l);
                    poiLabels.Add((p, l, LabelHeight(p.model)));
                }

            built = true;
            dashboard.Build();
            RefreshButtons();
        }

        static float LabelHeight(RideModel m)
        {
            switch (m)
            {
                case RideModel.Coaster: return 22f;
                case RideModel.DropTower: return 51f;
                case RideModel.Swing: return 18f;
                case RideModel.LogFlume: return 12f;
                case RideModel.PirateShip: return 16f;
                case RideModel.DarkRide: return 21f;
                case RideModel.FerrisWheel: return 31f;
                case RideModel.Carousel: return 11f;
                case RideModel.Teacups: return 6f;
                case RideModel.BumperCars: return 7f;
                case RideModel.KiddieTrain: return 4f;
                case RideModel.MiniCoaster: return 6f;
                case RideModel.FoodCourt: return 6f;
                case RideModel.SnackBar: return 5f;
                default: return 3f;
            }
        }

        public static string KindIcon(Poi p) => p.kind == PoiKind.Ride ? "●" : p.kind == PoiKind.Food ? "◆" : "■";

        // ---------- actions ----------

        void OnStartPause()
        {
            if (app == null) return;
            var r = app.runner;
            if (r.running) r.Pause(); else r.Play();
            RefreshButtons();
        }

        void OnReset()
        {
            if (app == null) return;
            bool wasRunning = app.runner.running;
            app.runner.ResetSim();
            if (wasRunning) app.runner.Play();
            applyNote.text = "";
            RefreshAll();
        }

        void SetSpeed(float v)
        {
            if (app == null) return;
            app.runner.speed = Mathf.Clamp(v, 1f, 600f);
            speedSlider.SetValueWithoutNotify(Mathf.Log10(app.runner.speed));
            RefreshButtons();
        }

        void OnViewChanged() { RefreshButtons(); dashboard?.SetVisible(app.view == ViewMode.Control); lastLeft = -1f; }

        // ---------- refresh ----------

        void RefreshButtons()
        {
            if (!built || app == null) return;
            var r = app.runner;
            startBtn.text = r.running ? Loc.T("Pause") : Loc.T("Start");
            speedLabel.text = Loc.F("Speed {0}x  (1 s = {1})", F0(r.speed), Dur(r.speed));
            float sv = Mathf.Log10(Mathf.Max(1f, r.speed));
            if (Mathf.Abs(speedSlider.value - sv) > 0.001f) speedSlider.SetValueWithoutNotify(sv);
            viewGuest.style.backgroundColor = app.view == ViewMode.Guest ? BtnOn : Btn;
            viewControl.style.backgroundColor = app.view == ViewMode.Control ? BtnOn : Btn;
            if (app.visitorsView != null) foreach (var kv in colorBtns) kv.Value.style.backgroundColor = kv.Key == app.visitorsView.colorBy ? BtnOn : Btn;
            if (app.overlay != null)
            {
                foreach (var kv in heatBtns) kv.Value.style.backgroundColor = kv.Key == app.overlay.heat ? BtnOn : Btn;
                foreach (var kv in edgeBtns) kv.Value.style.backgroundColor = kv.Key == app.overlay.edges ? BtnOn : Btn;
            }
            labelsBtn.style.backgroundColor = showLabels ? BtnOn : Btn;
            langKo.style.backgroundColor = Loc.Korean ? BtnOn : Btn;
            langEn.style.backgroundColor = Loc.Korean ? Btn : BtnOn;
            legend.text = Legend();
            int vi = System.Array.IndexOf(VisitorChoices, r.parameters.dailyVisitors);
            if (vi >= 0) visitorsDrop.SetValueWithoutNotify(visitorsDrop.choices[vi]);
            dashboard?.SetVisible(app.view == ViewMode.Control);
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        string Legend()
        {
            var by = app.visitorsView != null ? app.visitorsView.colorBy : ColorBy.State;
            if (by == ColorBy.Type)
                return $"<color=#{Hex(VisitorRenderer.TypeColors[0])}>●</color> {Loc.T("Family")}  <color=#{Hex(VisitorRenderer.TypeColors[1])}>●</color> {Loc.T("Teen")}  <color=#{Hex(VisitorRenderer.TypeColors[2])}>●</color> {Loc.T("Adult")}";
            if (by == ColorBy.Guide)
                return $"<color=#{Hex(VisitorRenderer.GuideColors[0])}>●</color> {Loc.T("Uses guide app")}  <color=#{Hex(VisitorRenderer.GuideColors[1])}>●</color> {Loc.T("No app")}";
            var c = VisitorRenderer.StateColors;
            return $"<color=#{Hex(c[0])}>●</color> {Loc.T("Walking")}  <color=#{Hex(c[1])}>●</color> {Loc.T("Queueing")}  <color=#{Hex(c[2])}>●</color> {Loc.T("Eating")}  " +
                   $"<color=#{Hex(c[3])}>●</color> {Loc.T("Resting")}  <color=#{Hex(c[5])}>●</color> {Loc.T("Browsing")}  <color=#{Hex(c[4])}>●</color> {Loc.T("Leaving")}  <color=#{Hex(VisitorRenderer.Highlight)}>●</color> {Loc.T("Selected")}";
        }

        public static string WaitColor(float s) => s < 15 * 60 ? "#59D973" : s < 40 * 60 ? "#FFB333" : "#FF594D";

        string RouteLine(string title, GuideResult g, bool best)
        {
            if (g == null || !g.ok) return "";
            string star = best ? " ★" : "";
            return $"<b>{title}{star}</b>  {F0(g.length)} m · {Loc.T("walk")} {Dur(g.walkTime)}" +
                   (g.crowdDelay > 5f ? $" <color=#FFB333>(+{Dur(g.crowdDelay)})</color>" : "") +
                   $"\n   {Loc.T("wait")} <color={WaitColor(g.wait)}>{Dur(g.wait)}</color> → <b>{Loc.T("total")} {Dur(g.Total)}</b> · {Loc.T("board at")} {Clock(g.boardClock)}" +
                   $" · LOS {Walk.LosNames[g.worstLos]}";
        }

        void RefreshGuideText()
        {
            if (!built || app == null) return;
            var L0 = app.Layout;
            var v = app.SelectedVisitor;
            if (v != null)
            {
                string doing = Loc.T(v.state == VState.Walking ? (v.goal == Goal.Exit ? "leaving" : "walking") : v.state.ToString().ToLowerInvariant());
                string to = v.goal == Goal.Browse ? Loc.T("strolling") : v.goal == Goal.Exit || v.target < 0 ? Loc.T("Main Gate") : Loc.T(L0.pois[v.target].name);
                startInfo.text = $"{Loc.T("Start")}: <color=#{Hex(VisitorRenderer.Highlight)}>{Loc.F("Visitor #{0}", v.id)}</color> · {Loc.T(v.type.ToString())} · {doing} → {to}\n" +
                                 $"   {Loc.F("{0} rides, {1} walked", v.rides, Km(v.distance))}{(v.guided ? " · " + Loc.T("Uses guide app") : "")}";
            }
            else
            {
                var sp = app.startPoint;
                var gate = L0.nodes[L0.gateNode].pos;
                startInfo.text = (sp - gate).magnitude < 8f ? $"{Loc.T("Start")}: {Loc.T("Main Gate")}" : $"{Loc.T("Start")}: {Loc.T("Map point")} ({F0(sp.x)}, {F0(sp.y)})";
            }
            int di = destIds.IndexOf(app.destPoi);
            if (di >= 0 && destDrop.index != di) destDrop.SetValueWithoutNotify(destDrop.choices[di]);
            var f = app.fastest; var s = app.shortest;
            if (app.destPoi < 0 || f == null || !f.ok) { guideOut.text = app.destPoi < 0 ? Loc.T("Choose a destination to see the route.") : Loc.T("No route found."); return; }
            var poi = L0.pois[app.destPoi];
            string head = $"<b>{Loc.T(poi.name)}</b>";
            if (poi.kind == PoiKind.Ride)
                head += $"  · {Loc.T("posted wait")} <color={WaitColor(f.waitNow)}>{Dur(f.waitNow)}</color> · {Loc.F("{0}/h", F0(poi.Throughput))}";
            bool same = s != null && AppController.SameRoute(f, s);
            string body = same
                ? RouteLine(Loc.T("Route (shortest = fastest)"), f, true)
                : RouteLine(Loc.T("Fastest"), f, f.Total <= s.Total + 0.5f) + "\n" + RouteLine(Loc.T("Shortest"), s, s.Total < f.Total - 0.5f);
            guideOut.text = head + "\n" + body;
        }

        public static string Km(float m) => m >= 1000f ? $"{(m / 1000f).ToString("0.00", CultureInfo.InvariantCulture)} km" : $"{F0(m)} m";

        void Update()
        {
            if (!built || app == null) return;
            UpdateViewport();
            UpdateLabels();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        void UpdateViewport()
        {
            var rig = app.cameraRig;
            if (rig == null || root == null || root.worldBound.width < 1f) return;
            float left = Mathf.Clamp(panel.worldBound.width / root.worldBound.width, 0f, 0.5f);
            float right = dashboard.Visible ? Mathf.Clamp(dashboard.worldBound.width / root.worldBound.width, 0f, 0.5f) : 0f;
            if (Mathf.Abs(left - lastLeft) > 0.002f || Mathf.Abs(right - lastRight) > 0.002f)
            {
                rig.viewportLeft = left; rig.viewportRight = right;
                bool first = lastLeft < 0f;
                lastLeft = left; lastRight = right;
                if (rig.mode == CameraRig.Mode.Map && first) rig.FrameMap();
            }
        }

        void UpdateLabels()
        {
            var rig = app.cameraRig;
            var sim = app.Sim;
            bool textNow = Time.unscaledTime >= nextLabelText;
            if (textNow) nextLabelText = Time.unscaledTime + 0.5f;
            float rootW = root.worldBound.width, rootH = root.worldBound.height;
            foreach (var (p, l, h) in poiLabels)
            {
                bool show = showLabels && rig != null && labelLayer.panel != null;
                Vector2 sp = default;
                if (show)
                {
                    float hh = rig.mode == CameraRig.Mode.Map ? 0f : h;
                    show = rig.WorldToScreen(new Vector3(p.pos.x, hh, p.pos.y), out sp);
                }
                if (show)
                {
                    var pp = RuntimePanelUtils.ScreenToPanel(labelLayer.panel, new Vector2(sp.x, Screen.height - sp.y));
                    show = pp.x > panel.worldBound.width + 10f && pp.x < rootW - (dashboard.Visible ? dashboard.worldBound.width : 0f) - 40f && pp.y > 10f && pp.y < rootH - 10f;
                    if (rig.mode != CameraRig.Mode.Map && p.kind != PoiKind.Ride && rig.distance > 220f) show = false;
                    if (show) { l.style.left = pp.x - 40f; l.style.top = pp.y - 26f; }
                }
                l.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show && textNow)
                {
                    string name = Loc.T(p.name);
                    if (sim != null && p.kind == PoiKind.Ride)
                    {
                        float w = sim.poi[p.id].WaitNow(sim.t);
                        l.text = $"{name}  <color={WaitColor(w)}><b>{Mathf.RoundToInt(w / 60f)}</b>{Loc.T("min")}</color>";
                    }
                    else if (sim != null && p.kind == PoiKind.Food)
                        l.text = $"{name}  <color=#B0B8C0>{sim.poi[p.id].inside}/{p.capacity}</color>";
                    else l.text = name;
                    l.style.borderLeftWidth = app.destPoi == p.id ? 6 : 3;
                }
            }
        }

        void Refresh()
        {
            var r = app.runner; var s = app.Sim;
            RefreshButtons();
            if (s == null) { clock.text = "--:--"; return; }
            clock.text = Clock(s.t);
            var L0 = s.L;
            string st = s.t < L0.closeTime ? Loc.T("Open") : s.inPark > 0 ? Loc.T("Closing – visitors leaving") : Loc.T("Closed");
            status.text = $"{st}\n{Loc.F("{0} in park", N(s.inPark))}";
            float day = Mathf.InverseLerp(L0.openTime, L0.closeTime + 1800f, s.t);
            dayFill.style.width = Length.Percent(100f * day);
            simInfo.text = Loc.F("Entered {0} / {1} · queueing {2} · riding {3}", N(s.entered), N(s.DailyVisitors), N(s.nQueue), N(s.nRiding)) +
                           $"\n{Loc.F("Avg wait {0} · avg walk {1}", Dur(s.tel.AvgRideWait()), Km(s.tel.AvgDistanceAll()))}" +
                           $"\n<color=#8A919C>{Loc.F("{0} visitors/day · {1}% use the guide app · step {2} ms", N(s.DailyVisitors), F0(s.P.guidedShare * 100f), F1(r.LastStepMs))}</color>";
            dashboard?.Refresh();
        }
    }
}
