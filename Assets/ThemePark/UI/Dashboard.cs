using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace ThemePark.UI
{
    /// <summary>Control-room panel: KPIs, visitors over time, ride usage table, queue chart, congestion hot spots and zones,
    /// busiest walkways, distance distribution, guide accuracy and data export.</summary>
    public class Dashboard : VisualElement
    {
        readonly AppController app;
        readonly List<Label> kpiValue = new List<Label>(), kpiSub = new List<Label>();
        LineChart people, queues;
        VisualElement rideRows, hotRows, zoneRows, edgeRows;
        BarChart dist;
        Label eta, exportStatus;
        float nextRefresh;

        static readonly Color[] Palette =
        {
            new Color(0.30f, 0.65f, 1f), new Color(1f, 0.62f, 0.15f), new Color(0.35f, 0.85f, 0.45f), new Color(0.85f, 0.45f, 0.95f),
            new Color(1f, 0.35f, 0.35f), new Color(0.2f, 0.85f, 0.85f)
        };

        public Dashboard(AppController app)
        {
            this.app = app;
            style.width = 560;
            style.backgroundColor = ThemeParkUI.Bg;
            ThemeParkUI.Pad(this, 10);
            style.display = DisplayStyle.None;
        }

        public bool Visible => style.display == DisplayStyle.Flex;

        public void SetVisible(bool v)
        {
            var d = v ? DisplayStyle.Flex : DisplayStyle.None;
            if (style.display != d) { style.display = d; nextRefresh = 0f; }
        }

        static Label L(string t, float s = 12, Color? c = null, bool b = false) => ThemeParkUI.L(t, s, c, b);

        public void Build()
        {
            Clear();
            kpiValue.Clear(); kpiSub.Clear();
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.name = "DashboardScroll";
            Add(scroll);
            var title = L(Loc.T("Control room"), 20, ThemeParkUI.Accent, true);
            scroll.Add(title);
            scroll.Add(L(Loc.T("Movement data collected from every visitor, updated live."), 12, ThemeParkUI.Dim));

            // KPI tiles
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginTop = 6; grid.style.marginBottom = 4;
            foreach (var k in new[] { "In park now", "Entered today", "Peak in park", "Avg ride wait", "Avg distance walked", "Rides taken" })
            {
                var t = new VisualElement();
                t.style.width = Length.Percent(32.3f);
                t.style.marginRight = Length.Percent(1f);
                t.style.marginBottom = 6;
                t.style.backgroundColor = ThemeParkUI.Card;
                ThemeParkUI.Radius(t, 7); ThemeParkUI.Pad(t, 8);
                t.Add(L(Loc.T(k), 11, ThemeParkUI.Dim));
                var v = L("-", 20, ThemeParkUI.Text, true); t.Add(v); kpiValue.Add(v);
                var s = L("", 10, ThemeParkUI.Dim); t.Add(s); kpiSub.Add(s);
                grid.Add(t);
            }
            scroll.Add(grid);

            var c1 = ThemeParkUI.Section(scroll, Loc.T("Visitors over time"));
            people = new LineChart(120);
            c1.Add(people); c1.Add(people.legend);

            var c2 = ThemeParkUI.Section(scroll, Loc.T("Rides: usage and waiting"));
            c2.Add(TableRow(new[] { Loc.T("Ride"), Loc.T("Queue"), Loc.T("Wait now"), Loc.T("Riders"), Loc.T("Use %"), Loc.T("Avg wait") }, true));
            rideRows = new VisualElement(); c2.Add(rideRows);

            var c3 = ThemeParkUI.Section(scroll, Loc.T("Queue length (top 5 rides)"));
            queues = new LineChart(110);
            c3.Add(queues); c3.Add(queues.legend);

            var c4 = ThemeParkUI.Section(scroll, Loc.T("Congestion hot spots (live, 6 x 6 m)"));
            hotRows = new VisualElement(); c4.Add(hotRows);
            c4.Add(L(Loc.T("Zones"), 11, ThemeParkUI.Dim, true));
            c4.Add(TableRow(new[] { Loc.T("Zone"), Loc.T("People"), Loc.T("p/m²"), Loc.T("LOS"), Loc.T("Peak"), "" }, true, null, ZoneW));
            zoneRows = new VisualElement(); c4.Add(zoneRows);

            var c5 = ThemeParkUI.Section(scroll, Loc.T("Busiest walkways (flow per hour)"));
            edgeRows = new VisualElement(); c5.Add(edgeRows);

            var c6 = ThemeParkUI.Section(scroll, Loc.T("Distance walked per visitor"));
            dist = new BarChart(90) { color = new Color(0.35f, 0.75f, 1f) };
            c6.Add(dist);

            var c7 = ThemeParkUI.Section(scroll, Loc.T("Guide accuracy (predicted vs actual)"));
            eta = L("", 12); c7.Add(eta);

            var c8 = ThemeParkUI.Section(scroll, Loc.T("Export"));
            var row = ThemeParkUI.Row();
            row.Add(ThemeParkUI.B(Loc.T("Export CSV / JSON"), Export));
            c8.Add(row);
            exportStatus = L("", 11, ThemeParkUI.Dim); c8.Add(exportStatus);
            nextRefresh = 0f;
        }

        static readonly float[] ColW = { 30f, 10f, 14f, 13f, 12f, 14f };
        static readonly float[] ZoneW = { 30f, 12f, 12f, 9f, 30f, 0f };

        static VisualElement TableRow(string[] cells, bool header, Color? first = null, float[] widths = null)
        {
            var ColW = widths ?? Dashboard.ColW;
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.marginBottom = 1;
            for (int i = 0; i < cells.Length; i++)
            {
                string text = !header && i == 0 && first.HasValue ? $"<color=#{ColorUtility.ToHtmlStringRGB(first.Value)}>■</color> {cells[i]}" : cells[i];
                var l = L(text, header ? 10 : 11, header ? ThemeParkUI.Dim : ThemeParkUI.Text, header);
                l.style.width = Length.Percent(ColW[Mathf.Min(i, ColW.Length - 1)]);
                l.style.whiteSpace = WhiteSpace.NoWrap;
                l.style.overflow = Overflow.Hidden;
                if (i > 0) l.style.unityTextAlign = TextAnchor.MiddleRight;
                r.Add(l);
            }
            return r;
        }

        static string F0(float v) => v.ToString("0", CultureInfo.InvariantCulture);
        static string F2(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        string EdgeName(ParkLayout L, PathEdge e)
        {
            string N(int n)
            {
                var nm = L.nodes[n].name;
                if (nm.StartsWith("@")) return Loc.T(nm.Substring(1));
                if (nm == "GATE") return Loc.T("Main Gate");
                if (nm.StartsWith("H") && nm.Length == 2)
                {
                    var dirs = new[] { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
                    return Loc.T("Hub") + " " + Loc.T(dirs[nm[1] - '0']);
                }
                if (nm.StartsWith("M") && nm.Length == 2) return Loc.T("Main St.") + " " + nm[1];
                return nm;
            }
            string a = N(e.a), b = N(e.b);
            if (L.nodes[e.b].name.StartsWith("@")) return Loc.F("Path to {0}", b);
            if (L.nodes[e.a].name.StartsWith("@")) return Loc.F("Path to {0}", a);
            return $"{Loc.T(L.zones[e.zone].name)} ({a}–{b})";
        }

        public void Refresh()
        {
            if (!Visible || app == null || app.Sim == null) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            var s = app.Sim; var tel = s.tel; var L0 = s.L;
            float nowH = s.t / 3600f;

            // KPIs
            kpiValue[0].text = ThemeParkUI.N(s.inPark);
            kpiSub[0].text = Loc.F("walking {0} · queue {1}", ThemeParkUI.N(s.nWalking), ThemeParkUI.N(s.nQueue));
            kpiValue[1].text = ThemeParkUI.N(s.entered);
            kpiSub[1].text = Loc.F("left {0}", ThemeParkUI.N(s.exited));
            kpiValue[2].text = ThemeParkUI.N(tel.peakInPark);
            kpiSub[2].text = tel.peakInPark > 0 ? Loc.F("at {0}", ThemeParkUI.Clock(tel.peakInParkT)) : "";
            kpiValue[3].text = ThemeParkUI.Dur(tel.AvgRideWait());
            kpiSub[3].text = Loc.F("{0} boarded", ThemeParkUI.N(tel.totalRides));
            kpiValue[4].text = ThemeParkUI.Km(tel.AvgDistanceAll());
            kpiSub[4].text = tel.exitDistance.Count > 0 ? Loc.F("leavers {0}", ThemeParkUI.Km(tel.AvgExitDistance())) : "";
            kpiValue[5].text = ThemeParkUI.N(tel.totalRides);
            kpiSub[5].text = s.entered > 0 ? Loc.F("{0} per visitor", (tel.totalRides / (float)s.entered).ToString("0.0", CultureInfo.InvariantCulture)) : "";

            // visitors over time
            people.series.Clear();
            people.series.Add(new LineChart.Series { y = tel.inPark, color = Palette[0], name = Loc.T("In park") });
            people.series.Add(new LineChart.Series { y = tel.queueing, color = Palette[1], name = Loc.T("Queueing") });
            people.series.Add(new LineChart.Series { y = tel.walking, color = Palette[2], name = Loc.T("Walking") });
            people.series.Add(new LineChart.Series { y = tel.riding, color = Palette[3], name = Loc.T("Riding") });
            people.series.Add(new LineChart.Series { y = tel.browsing, color = Palette[5], name = Loc.T("Browsing") });
            people.now = nowH;
            people.SetData(tel.times);

            // ride table
            rideRows.Clear();
            var rides = new List<PoiState>();
            foreach (var st in s.poi) if (st.p.kind == PoiKind.Ride) rides.Add(st);
            rides.Sort((a, b) => b.WaitNow(s.t).CompareTo(a.WaitNow(s.t)));
            float openH = Mathf.Max(1f / 60f, (Mathf.Min(s.t, L0.closeTime) - L0.openTime) / 3600f);
            foreach (var st in rides)
            {
                float w = st.WaitNow(s.t);
                float use = st.riders / Mathf.Max(1f, st.p.Throughput * openH) * 100f;
                var row = TableRow(new[]
                {
                    Loc.T(st.p.name), st.queue.Count.ToString(), $"<color={ThemeParkUI.WaitColor(w)}>{ThemeParkUI.Dur(w)}</color>",
                    ThemeParkUI.N(st.riders), F0(use) + "%", ThemeParkUI.Dur(st.AvgWait)
                }, false, st.p.color);
                if (app.destPoi == st.p.id) row.style.backgroundColor = new Color(0.13f, 0.55f, 0.78f, 0.25f);
                int id = st.p.id;
                row.RegisterCallback<ClickEvent>(e => app.SetDestination(id));
                rideRows.Add(row);
            }

            // queues chart (top 5 current)
            queues.series.Clear();
            for (int i = 0; i < Mathf.Min(5, rides.Count); i++)
                queues.series.Add(new LineChart.Series { y = tel.queueLen[rides[i].p.id], color = Palette[i % Palette.Length], name = Loc.T(rides[i].p.name) });
            queues.now = nowH;
            queues.SetData(tel.times);

            // hot spots
            hotRows.Clear();
            var hs = tel.Hotspots(5);
            if (hs.Count == 0) hotRows.Add(L(Loc.T("No crowding right now."), 11, ThemeParkUI.Dim));
            foreach (var h in hs)
            {
                int los = Walk.Los(h.density);
                string where = h.poi >= 0 ? Loc.T(L0.pois[h.poi].name) : Loc.T(L0.zones[h.zone].name);
                var l = L($"<color=#{ColorUtility.ToHtmlStringRGB(Walk.LosColors[los])}>■ {Walk.LosNames[los]}</color>  {where} · {F2(h.density)} p/m² · {Loc.F("{0} people", h.people)}", 11);
                var pos = h.pos;
                l.RegisterCallback<ClickEvent>(e => app.cameraRig.LookAt(new Vector3(pos.x, 0f, pos.y), 60f));
                hotRows.Add(l);
            }
            tel.CountZones();
            zoneRows.Clear();
            foreach (var z in L0.zones)
            {
                float d = tel.zoneCount[z.id] / Mathf.Max(1f, z.area);
                int los = Walk.Los(d);
                zoneRows.Add(TableRow(new[]
                {
                    Loc.T(z.name), ThemeParkUI.N(tel.zoneCount[z.id]), F2(d),
                    $"<color=#{ColorUtility.ToHtmlStringRGB(Walk.LosColors[los])}>{Walk.LosNames[los]}</color>",
                    tel.zonePeak[z.id] > 0 ? $"{F2(tel.zonePeak[z.id])} ({ThemeParkUI.Clock(tel.zonePeakT[z.id])})" : "-", ""
                }, false, z.color, ZoneW));
            }

            // busiest walkways
            edgeRows.Clear();
            var order = new List<int>();
            for (int e = 0; e < L0.edges.Count; e++) order.Add(e);
            order.Sort((a, b) => tel.edgeRate[b].CompareTo(tel.edgeRate[a]));
            for (int i = 0; i < Mathf.Min(6, order.Count); i++)
            {
                var e = L0.edges[order[i]];
                int los = Walk.Los(s.edgeDensity[e.id]);
                edgeRows.Add(L($"<color=#{ColorUtility.ToHtmlStringRGB(OverlayRenderer.FlowColor(tel.edgeRate[e.id]))}>━</color> {EdgeName(L0, e)} · " +
                               $"<b>{ThemeParkUI.N(Mathf.RoundToInt(tel.edgeRate[e.id]))}/h</b> · {Loc.T("total")} {ThemeParkUI.N(tel.edgeFlow[e.id])} · LOS {Walk.LosNames[los]} ({e.width:0.#} m)", 11));
            }

            // distance histogram
            var hist = tel.DistanceHistogram();
            var vals = new float[hist.Length]; var names = new string[hist.Length];
            for (int i = 0; i < hist.Length; i++)
            {
                vals[i] = hist[i];
                names[i] = i == hist.Length - 1 ? $"{(i * Telemetry.DistBin / 1000f):0.#}+" : $"{(i * Telemetry.DistBin / 1000f):0.#}";
            }
            dist.SetData(vals, names);

            // guide accuracy
            var all = tel.Eta(); var gu = tel.Eta(1); var ng = tel.Eta(0);
            eta.text = all.n == 0 ? Loc.T("Waiting for the first completed trips...") :
                Loc.F("{0} trips to rides (departure → boarding).", ThemeParkUI.N(all.n)) + "\n" +
                Loc.F("Walking time error: {0} (bias {1})", ThemeParkUI.Dur(all.walkMae), (all.walkBias >= 0 ? "+" : "-") + ThemeParkUI.Dur(Mathf.Abs(all.walkBias))) + "\n" +
                Loc.F("Wait error: posted wait {0} → forecast {1}", ThemeParkUI.Dur(all.waitMaeNaive), ThemeParkUI.Dur(all.waitMaeForecast)) + "\n" +
                Loc.F("Total time error (forecast): {0} ({1}%)", ThemeParkUI.Dur(all.totalMaeForecast), F0(all.totalMapeForecast * 100f)) + "\n" +
                (gu.n > 0 && ng.n > 0 ? Loc.F("Ride waits: app users {0} · others {1}", ThemeParkUI.Dur(AvgWait(s, true)), ThemeParkUI.Dur(AvgWait(s, false))) : "");
        }

        static float AvgWait(ParkSim s, bool guided)
        {
            double sum = 0; int n = 0;
            foreach (var r in s.tel.trips) if (r.stage == 2 && r.guided == guided) { sum += r.wait; n++; }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        void Export()
        {
            var s = app?.Sim; if (s == null) return;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Exports", System.DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            var files = s.tel.Export(folder);
            exportStatus.text = Loc.F("{0} files written to {1}", files.Count, folder);
        }
    }
}
