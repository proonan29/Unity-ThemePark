using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace ThemePark
{
    public enum EvType : byte { Arrive, Join, Board, RideEnd, FoodStart, FoodEnd, RestStart, RestEnd, Exit }

    public struct LogEvent
    {
        public float t;
        public int visitor;
        public EvType type;
        public short poi;
    }

    /// <summary>One trip to a ride: what the guide predicted when the visitor set off vs. what happened.</summary>
    public struct TripRecord
    {
        public int visitor, poi;
        public bool guided;
        public float t0, predWalk, walk, predWaitNaive, predWaitForecast, wait;
        public byte stage;       // 0 walking, 1 queueing, 2 boarded
    }

    public struct Hotspot
    {
        public Vector2 pos;
        public float density;    // persons/m² over a 6 x 6 m window
        public int people;
        public int poi;          // nearest POI within 25 m (-1 = none)
        public int zone;
    }

    /// <summary>Collects movement data from the simulation: heat map, time series, ride/edge/zone statistics,
    /// trip ETA validation and an event log; exports CSV/JSON.</summary>
    public class Telemetry
    {
        public const float SampleEvery = 60f;
        public const float Cell = 2f;
        public const int Hours = 14;            // 09:00 .. 23:00
        public const float DistBin = 500f;      // m
        public const int DistBins = 14;

        readonly ParkSim sim;
        readonly ParkLayout L;
        public readonly int gw, gh;
        public readonly Vector2 origin;
        public readonly float[] heatCum, heatLive;
        readonly float[] heatNow;
        readonly ParkSim.VisibleFn heatFn;

        public readonly List<float> times = new List<float>();          // hours of day
        public readonly List<float> inPark = new List<float>(), walking = new List<float>(), queueing = new List<float>(),
            riding = new List<float>(), eating = new List<float>(), resting = new List<float>(), browsing = new List<float>();
        public readonly List<float>[] queueLen, postedWait;
        public readonly int[][] ridersByHour;
        public readonly int[] edgeFlow;
        readonly int[] edgeWindow;
        public readonly float[] edgeRate;            // persons/h (smoothed)
        public readonly float[] edgePeak, edgePeakT;
        public readonly float[] zoneNow, zonePeak, zonePeakT;
        public readonly int[] zoneCount;
        public readonly List<float>[] zoneSeries;
        public readonly List<float> exitDistance = new List<float>();
        public readonly List<int> exitRides = new List<int>();
        public readonly List<TripRecord> trips = new List<TripRecord>();
        public readonly List<LogEvent> events = new List<LogEvent>();
        public int peakInPark;
        public float peakInParkT;
        public int totalRides;
        public bool logEvents = true;

        public Telemetry(ParkSim s)
        {
            sim = s; L = s.L;
            origin = new Vector2(L.bounds.xMin, L.bounds.yMin);
            gw = Mathf.CeilToInt(L.bounds.width / Cell);
            gh = Mathf.CeilToInt(L.bounds.height / Cell);
            heatCum = new float[gw * gh]; heatLive = new float[gw * gh]; heatNow = new float[gw * gh];
            heatFn = (v, p, f) =>
            {
                int x = (int)((p.x - origin.x) / Cell), y = (int)((p.y - origin.y) / Cell);
                if (x >= 0 && y >= 0 && x < gw && y < gh) heatNow[y * gw + x] += 1f;
            };
            int np = L.pois.Count;
            queueLen = new List<float>[np]; postedWait = new List<float>[np]; ridersByHour = new int[np][];
            for (int i = 0; i < np; i++) { queueLen[i] = new List<float>(); postedWait[i] = new List<float>(); ridersByHour[i] = new int[Hours]; }
            int ne = L.edges.Count;
            edgeFlow = new int[ne]; edgeWindow = new int[ne]; edgeRate = new float[ne]; edgePeak = new float[ne]; edgePeakT = new float[ne];
            int nz = L.zones.Count;
            zoneNow = new float[nz]; zonePeak = new float[nz]; zonePeakT = new float[nz]; zoneCount = new int[nz];
            zoneSeries = new List<float>[nz];
            for (int i = 0; i < nz; i++) zoneSeries[i] = new List<float>();
        }

        public int CellIndex(Vector2 p)
        {
            int x = (int)((p.x - origin.x) / Cell), y = (int)((p.y - origin.y) / Cell);
            return x >= 0 && y >= 0 && x < gw && y < gh ? y * gw + x : -1;
        }

        // ------------------------------------------------------------------ hooks from the simulation

        public void Log(EvType type, Visitor v, int poiId)
        {
            if (!logEvents) return;
            events.Add(new LogEvent { t = sim.t, visitor = v.id, type = type, poi = (short)poiId });
        }

        public int StartTrip(Visitor v, int poiId, float predWalk, float predWaitNaive, float predWaitForecast)
        {
            trips.Add(new TripRecord
            {
                visitor = v.id, poi = poiId, guided = v.guided, t0 = sim.t, predWalk = predWalk,
                predWaitNaive = predWaitNaive, predWaitForecast = predWaitForecast
            });
            return trips.Count - 1;
        }

        public void Arrived(Visitor v, float walkTime)
        {
            if (v.trip < 0 || v.trip >= trips.Count) return;
            var r = trips[v.trip];
            if (r.stage != 0) return;
            r.walk = walkTime; r.stage = 1;
            trips[v.trip] = r;
        }

        public void Boarded(Visitor v, int poiId, float wait)
        {
            int h = Mathf.Clamp(Mathf.FloorToInt((sim.t - L.openTime) / 3600f), 0, Hours - 1);
            ridersByHour[poiId][h]++;
            totalRides++;
            Log(EvType.Board, v, poiId);
            if (v.trip >= 0 && v.trip < trips.Count)
            {
                var r = trips[v.trip];
                if (r.stage == 1 && r.poi == poiId) { r.wait = wait; r.stage = 2; trips[v.trip] = r; }
            }
            v.trip = -1;
        }

        public void Dispatched(int poiId, int n) { }

        public void EdgeTraversed(int e) { edgeFlow[e]++; edgeWindow[e]++; }

        public void Exited(Visitor v)
        {
            exitDistance.Add(v.distance);
            exitRides.Add(v.rides);
            Log(EvType.Exit, v, -1);
        }

        public void AccumulateHeat(float dt)
        {
            System.Array.Clear(heatNow, 0, heatNow.Length);
            sim.ForEachVisible(heatFn);
            float a = 1f - Mathf.Exp(-dt / 20f);
            for (int i = 0; i < heatNow.Length; i++)
            {
                heatCum[i] += heatNow[i] * dt;
                heatLive[i] += (heatNow[i] - heatLive[i]) * a;
            }
        }

        public void Sample()
        {
            float t = sim.t;
            sim.RecountNow();
            times.Add(t / 3600f);
            inPark.Add(sim.inPark); walking.Add(sim.nWalking); queueing.Add(sim.nQueue);
            riding.Add(sim.nRiding); eating.Add(sim.nEating); resting.Add(sim.nResting); browsing.Add(sim.nBrowsing);
            if (sim.inPark > peakInPark) { peakInPark = sim.inPark; peakInParkT = t; }
            foreach (var st in sim.poi)
            {
                int i = st.p.id;
                queueLen[i].Add(st.queue.Count);
                postedWait[i].Add(st.p.kind == PoiKind.Rest ? 0f : st.WaitNow(t) / 60f);
                st.inflow = 0.6f * st.inflow + 0.4f * st.joinedWindow / SampleEvery;
                st.joinedWindow = 0;
            }
            for (int e = 0; e < edgeRate.Length; e++)
            {
                edgeRate[e] = 0.7f * edgeRate[e] + 0.3f * edgeWindow[e] * (3600f / SampleEvery);
                edgeWindow[e] = 0;
                float d = sim.edgeDensity[e];
                if (d > edgePeak[e]) { edgePeak[e] = d; edgePeakT[e] = t; }
            }
            CountZones();
            for (int z = 0; z < zoneNow.Length; z++)
            {
                zoneNow[z] = zoneCount[z] / Mathf.Max(1f, L.zones[z].area);
                zoneSeries[z].Add(zoneCount[z]);
                if (zoneNow[z] > zonePeak[z]) { zonePeak[z] = zoneNow[z]; zonePeakT[z] = t; }
            }
        }

        public void CountZones()
        {
            System.Array.Clear(zoneCount, 0, zoneCount.Length);
            foreach (var v in sim.visitors)
            {
                switch (v.state)
                {
                    case VState.Walking:
                        if (v.edge >= 0) zoneCount[L.edges[v.edge].zone]++;
                        else zoneCount[L.nodes[v.node].zone]++;
                        break;
                    case VState.Browsing:
                        zoneCount[L.nodes[v.node].zone]++;
                        break;
                    case VState.Queue:
                    case VState.Eating:
                    case VState.Resting:
                        zoneCount[L.pois[v.target].zone]++;
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ derived

        /// <summary>Busiest 6 x 6 m spots right now, at least 18 m apart.</summary>
        public List<Hotspot> Hotspots(int max = 6, float minDensity = 0.15f)
        {
            var list = new List<Hotspot>();
            var cand = new List<(int idx, float sum)>();
            for (int y = 1; y < gh - 1; y++)
                for (int x = 1; x < gw - 1; x++)
                {
                    float s = 0f;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) s += heatLive[(y + dy) * gw + x + dx];
                    if (s / (9f * Cell * Cell) >= minDensity) cand.Add((y * gw + x, s));
                }
            cand.Sort((a, b) => b.sum.CompareTo(a.sum));
            foreach (var c in cand)
            {
                var p = origin + new Vector2((c.idx % gw + 0.5f) * Cell, (c.idx / gw + 0.5f) * Cell);
                bool near = false;
                foreach (var h in list) if ((h.pos - p).sqrMagnitude < 18f * 18f) { near = true; break; }
                if (near) continue;
                int best = -1; float bd = 25f * 25f;
                foreach (var poi in L.pois)
                {
                    float d = Mathf.Min((poi.pos - p).sqrMagnitude, (poi.entrance - p).sqrMagnitude);
                    if (d < bd) { bd = d; best = poi.id; }
                }
                int zone = best >= 0 ? L.pois[best].zone : L.edges[sim.G.Nearest(p).edge].zone;
                list.Add(new Hotspot { pos = p, density = c.sum / (9f * Cell * Cell), people = Mathf.RoundToInt(c.sum), poi = best, zone = zone });
                if (list.Count >= max) break;
            }
            return list;
        }

        public float AvgRideWait()
        {
            float s = 0f; int n = 0;
            foreach (var st in sim.poi) if (st.p.kind == PoiKind.Ride) { s += st.waitSum; n += st.waitN; }
            return n > 0 ? s / n : 0f;
        }

        public float AvgExitDistance()
        {
            if (exitDistance.Count == 0) return 0f;
            float s = 0f; foreach (var d in exitDistance) s += d;
            return s / exitDistance.Count;
        }

        /// <summary>Average distance walked by everyone who entered (exited + still in the park).</summary>
        public float AvgDistanceAll()
        {
            if (sim.visitors.Count == 0) return 0f;
            double s = 0; foreach (var v in sim.visitors) s += v.distance;
            return (float)(s / sim.visitors.Count);
        }

        public int[] DistanceHistogram()
        {
            var h = new int[DistBins];
            foreach (var v in sim.visitors) h[Mathf.Clamp((int)(v.distance / DistBin), 0, DistBins - 1)]++;
            return h;
        }

        public struct EtaStats
        {
            public int n;
            public float walkMae, walkBias, waitMaeNaive, waitMaeForecast, totalMaeNaive, totalMaeForecast, totalMapeForecast;
        }

        public EtaStats Eta(int guidedFilter = -1, int poiFilter = -1)
        {
            var e = new EtaStats();
            double w = 0, wb = 0, qn = 0, qf = 0, tn = 0, tf = 0, mape = 0; int nm = 0;
            foreach (var r in trips)
            {
                if (r.stage != 2) continue;
                if (guidedFilter >= 0 && r.guided != (guidedFilter == 1)) continue;
                if (poiFilter >= 0 && r.poi != poiFilter) continue;
                e.n++;
                w += Mathf.Abs(r.predWalk - r.walk); wb += r.predWalk - r.walk;
                qn += Mathf.Abs(r.predWaitNaive - r.wait); qf += Mathf.Abs(r.predWaitForecast - r.wait);
                float act = r.walk + r.wait;
                tn += Mathf.Abs(r.predWalk + r.predWaitNaive - act);
                tf += Mathf.Abs(r.predWalk + r.predWaitForecast - act);
                if (act > 120f) { mape += Mathf.Abs(r.predWalk + r.predWaitForecast - act) / act; nm++; }
            }
            if (e.n > 0)
            {
                e.walkMae = (float)(w / e.n); e.walkBias = (float)(wb / e.n);
                e.waitMaeNaive = (float)(qn / e.n); e.waitMaeForecast = (float)(qf / e.n);
                e.totalMaeNaive = (float)(tn / e.n); e.totalMaeForecast = (float)(tf / e.n);
            }
            if (nm > 0) e.totalMapeForecast = (float)(mape / nm);
            return e;
        }

        // ------------------------------------------------------------------ export

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string Clock(float t) { int s = Mathf.RoundToInt(t); return $"{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}"; }

        /// <summary>Write CSV/JSON files into <paramref name="folder"/>; returns the list of files.</summary>
        public List<string> Export(string folder)
        {
            Directory.CreateDirectory(folder);
            var files = new List<string>();
            void Write(string name, StringBuilder sb) { var p = Path.Combine(folder, name); File.WriteAllText(p, sb.ToString(), new UTF8Encoding(true)); files.Add(p); }

            var sb = new StringBuilder("time,in_park,walking,queueing,riding,eating,resting,browsing");
            foreach (var z in L.zones) sb.Append(",zone_").Append(z.name.Replace(' ', '_').Replace("&", "and"));
            sb.AppendLine();
            for (int i = 0; i < times.Count; i++)
            {
                sb.Append(Clock(times[i] * 3600f)).Append(',').Append(inPark[i]).Append(',').Append(walking[i]).Append(',').Append(queueing[i])
                  .Append(',').Append(riding[i]).Append(',').Append(eating[i]).Append(',').Append(resting[i]).Append(',').Append(browsing[i]);
                foreach (var zs in zoneSeries) sb.Append(',').Append(i < zs.Count ? zs[i] : 0);
                sb.AppendLine();
            }
            Write("timeline.csv", sb);

            sb = new StringBuilder("poi,kind,zone,riders_or_guests,throughput_per_h,avg_wait_min,max_wait_min,queue_now");
            for (int h = 0; h < Hours; h++) sb.Append($",h{9 + h:00}");
            sb.AppendLine();
            foreach (var st in sim.poi)
            {
                var p = st.p;
                if (p.kind == PoiKind.Gate) continue;
                sb.Append('"').Append(p.name).Append("\",").Append(p.kind).Append(",\"").Append(L.zones[p.zone].name).Append("\",")
                  .Append(st.riders).Append(',').Append(F(p.Throughput)).Append(',').Append(F(st.AvgWait / 60f)).Append(',')
                  .Append(F(st.waitMax / 60f)).Append(',').Append(st.queue.Count);
                for (int h = 0; h < Hours; h++) sb.Append(',').Append(ridersByHour[p.id][h]);
                sb.AppendLine();
            }
            Write("pois.csv", sb);

            sb = new StringBuilder("time");
            foreach (var p in L.pois) if (p.kind == PoiKind.Ride) sb.Append(",\"").Append(p.name).Append('"');
            sb.AppendLine();
            for (int i = 0; i < times.Count; i++)
            {
                sb.Append(Clock(times[i] * 3600f));
                foreach (var p in L.pois) if (p.kind == PoiKind.Ride) sb.Append(',').Append(queueLen[p.id][i]);
                sb.AppendLine();
            }
            Write("queues.csv", sb);

            sb = new StringBuilder("edge,from,to,zone,length_m,width_m,traversals,peak_density,peak_time,los_peak\n");
            foreach (var e in L.edges)
                sb.Append(e.id).Append(',').Append(L.nodes[e.a].name).Append(',').Append(L.nodes[e.b].name).Append(",\"").Append(L.zones[e.zone].name)
                  .Append("\",").Append(F(e.length)).Append(',').Append(F(e.width)).Append(',').Append(edgeFlow[e.id]).Append(',')
                  .Append(F(edgePeak[e.id])).Append(',').Append(Clock(edgePeakT[e.id])).Append(',').Append(Walk.LosNames[Walk.Los(edgePeak[e.id])]).AppendLine();
            Write("edges.csv", sb);

            sb = new StringBuilder("zone,area_m2,peak_density,peak_time,los_peak\n");
            foreach (var z in L.zones)
                sb.Append('"').Append(z.name).Append("\",").Append(F(z.area)).Append(',').Append(F(zonePeak[z.id])).Append(',')
                  .Append(Clock(zonePeakT[z.id])).Append(',').Append(Walk.LosNames[Walk.Los(zonePeak[z.id])]).AppendLine();
            Write("zones.csv", sb);

            sb = new StringBuilder("visitor,guided,poi,depart,pred_walk_s,walk_s,pred_wait_naive_s,pred_wait_forecast_s,wait_s\n");
            foreach (var r in trips)
            {
                if (r.stage != 2) continue;
                sb.Append(r.visitor).Append(',').Append(r.guided ? 1 : 0).Append(",\"").Append(L.pois[r.poi].name).Append("\",").Append(Clock(r.t0)).Append(',')
                  .Append(F(r.predWalk)).Append(',').Append(F(r.walk)).Append(',').Append(F(r.predWaitNaive)).Append(',')
                  .Append(F(r.predWaitForecast)).Append(',').Append(F(r.wait)).AppendLine();
            }
            Write("trips.csv", sb);

            sb = new StringBuilder("visitor,type,guided,arrive,leave_plan,distance_m,rides\n");
            foreach (var v in sim.visitors)
                sb.Append(v.id).Append(',').Append(v.type).Append(',').Append(v.guided ? 1 : 0).Append(',').Append(Clock(v.arriveT)).Append(',')
                  .Append(Clock(v.leaveT)).Append(',').Append(F(v.distance)).Append(',').Append(v.rides).AppendLine();
            Write("visitors.csv", sb);

            if (logEvents)
            {
                sb = new StringBuilder("time,visitor,event,poi\n");
                foreach (var ev in events)
                    sb.Append(Clock(ev.t)).Append(',').Append(ev.visitor).Append(',').Append(ev.type).Append(',')
                      .Append(ev.poi >= 0 ? L.pois[ev.poi].name : "").AppendLine();
                Write("events.csv", sb);
            }

            sb = new StringBuilder($"# cumulative person-seconds per {F(Cell)} m cell; row 0 = z {F(origin.y)}, col 0 = x {F(origin.x)}\n");
            for (int y = 0; y < gh; y++)
            {
                for (int x = 0; x < gw; x++) { if (x > 0) sb.Append(','); sb.Append(Mathf.RoundToInt(heatCum[y * gw + x])); }
                sb.AppendLine();
            }
            Write("heat_cumulative.csv", sb);

            var eta = Eta();
            sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"park\": \"{L.name}\", \"clock\": \"{Clock(sim.t)}\", \"daily_visitors\": {sim.DailyVisitors}, \"seed\": {sim.P.seed}, \"guided_share\": {F(sim.P.guidedShare)},");
            sb.AppendLine($"  \"entered\": {sim.entered}, \"exited\": {sim.exited}, \"in_park\": {sim.inPark}, \"peak_in_park\": {peakInPark}, \"peak_time\": \"{Clock(peakInParkT)}\",");
            sb.AppendLine($"  \"rides\": {totalRides}, \"avg_ride_wait_min\": {F(AvgRideWait() / 60f)}, \"avg_distance_m\": {F(AvgDistanceAll())},");
            sb.AppendLine($"  \"eta\": {{ \"trips\": {eta.n}, \"walk_mae_s\": {F(eta.walkMae)}, \"walk_bias_s\": {F(eta.walkBias)}, \"wait_mae_naive_s\": {F(eta.waitMaeNaive)}, \"wait_mae_forecast_s\": {F(eta.waitMaeForecast)}, \"total_mape_forecast\": {F(eta.totalMapeForecast)} }}");
            sb.AppendLine("}");
            Write("summary.json", sb);
            return files;
        }
    }
}
