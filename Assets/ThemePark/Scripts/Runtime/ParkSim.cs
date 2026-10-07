using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThemePark
{
    public enum VState { Walking, Queue, Riding, Eating, Resting, Browsing, Gone }
    public enum VType { Family, Teen, Adult }
    public enum Goal { Ride, Food, Rest, Exit, Browse }

    [Serializable]
    public class SimParams
    {
        public int dailyVisitors = 8000;
        public int seed = 1;
        public float dt = 0.5f;               // s
        public float guidedShare = 0.5f;      // share of visitors using the guide app (congestion-aware routes, forecast waits)
        public float walkSpeed = 1.3f, walkSpeedSd = 0.15f;
        public float stayMeanHours = 5.5f, staySdHours = 1.5f;
        public SimParams Clone() => (SimParams)MemberwiseClone();
    }

    public class Visitor
    {
        public int id;
        public VType type;
        public float v0, thrillPref;
        public bool guided;
        public float arriveT, leaveT;
        public VState state;
        public Goal goal;
        public int target = -1;             // POI id (-1 = exit)
        public int node;                    // node when not on an edge
        public readonly List<int> route = new List<int>();
        public readonly List<int> routeEdges = new List<int>();
        public int leg, edge = -1;
        public float s, lateral;
        public Vector2 pos, heading = Vector2.up, spot;
        public float distance;
        public int rides;
        public float hunger, fatigue, hungerLimit, fatigueLimit;
        public float until, queueStart, tripStart;
        public int lastPoi = -1, trip = -1;
        public byte[] ridden;
        public bool Active => state != VState.Gone;
    }

    /// <summary>Live state of a ride, food outlet or rest area.</summary>
    public class PoiState
    {
        public Poi p;
        public readonly List<int> queue = new List<int>();   // visitor ids, head at index 0
        public float nextDispatch = float.NegativeInfinity, lastDispatch = -1e9f;
        public int riding, inside;
        public int riders, joined;
        public float waitSum, waitMax;
        public int waitN;
        public float inflow;                 // persons/s joining (EMA)
        public int joinedWindow;
        public float AvgWait => waitN > 0 ? waitSum / waitN : 0f;
        public float LoadWait => Mathf.Min(ParkSim.MaxLoadWait, p.interval * 0.5f);
        public float AvgService => 0.5f * (p.serviceMin + p.serviceMax);

        /// <summary>Waiting time for a person joining behind <paramref name="ahead"/> people at time t.</summary>
        public float WaitAt(float ahead, float t)
        {
            if (p.kind == PoiKind.Ride)
            {
                float first;
                if (queue.Count == 0 && t >= nextDispatch) first = Mathf.Max(lastDispatch + p.interval - t, LoadWait);
                else first = Mathf.Max(0f, nextDispatch - t);
                return first + Mathf.Floor(ahead / p.batch) * p.interval;
            }
            if (p.kind == PoiKind.Food)
            {
                if (inside < p.capacity && ahead <= 0f) return 0f;
                return (ahead + 1f) * AvgService / Mathf.Max(1, p.capacity);
            }
            return 0f;
        }

        /// <summary>Posted (naive) wait: the queue as it is now.</summary>
        public float WaitNow(float t) => WaitAt(queue.Count, t);

        /// <summary>Forecast wait for someone arriving after <paramref name="walk"/> seconds: queue drift from recent inflow vs. capacity.</summary>
        public float WaitForecast(float t, float walk)
        {
            if (p.kind != PoiKind.Ride) return WaitAt(queue.Count, t);
            float mu = p.batch / p.interval;
            float q = Mathf.Max(0f, queue.Count + (inflow - mu) * walk);
            if (q < 0.5f) return LoadWait * 0.5f;
            float tA = t + walk;
            float nd = queue.Count > 0 ? nextDispatch : Mathf.Max(nextDispatch, t + LoadWait);
            if (nd < tA) nd += Mathf.Ceil((tA - nd) / p.interval) * p.interval;
            return (nd - tA) + Mathf.Floor(q / p.batch) * p.interval;
        }
    }

    /// <summary>Serpentine queue slots inside the queue area; overflow spills back along the spur, two abreast.</summary>
    public static class QueueLayout
    {
        public const float Lane = 0.8f, Pitch = 0.6f;

        public static int Capacity(Poi p)
        {
            int lanes = Mathf.Max(1, Mathf.FloorToInt((p.queueDepth - 0.6f) / Lane));
            int per = Mathf.Max(1, Mathf.FloorToInt((p.QueueWidth - 0.6f) / Pitch));
            return lanes * per;
        }

        /// <summary>Slot i (0 = head, next to the building). Returns XZ position and facing (towards the head).</summary>
        public static Vector2 Slot(Poi p, int i, out Vector2 facing)
        {
            var right = new Vector2(p.dir.y, -p.dir.x);
            float w = p.QueueWidth;
            int per = Mathf.Max(1, Mathf.FloorToInt((w - 0.6f) / Pitch));
            int lanes = Mathf.Max(1, Mathf.FloorToInt((p.queueDepth - 0.6f) / Lane));
            if (p.queueDepth >= 2f && i < per * lanes)
            {
                int lane = i / per, k = i % per;
                bool even = (lane & 1) == 0;
                float x = even ? -w * 0.5f + 0.3f + k * Pitch : w * 0.5f - 0.3f - k * Pitch;
                float y = p.queueDepth - 0.4f - lane * Lane;
                facing = even ? -right : right;
                return p.entrance + right * x + p.dir * y;
            }
            int j = p.queueDepth >= 2f ? i - per * lanes : i;
            float back = 0.6f + (j / 2) * Pitch;
            float side = (j & 1) == 0 ? -0.35f : 0.35f;
            if (p.overflow != null && p.overflow.Length >= 2)
            {
                // follow the walkway back towards the gate, keeping to one side so others can pass
                for (int k = 0; k + 1 < p.overflow.Length; k++)
                {
                    var a = p.overflow[k]; var b = p.overflow[k + 1];
                    float len = Vector2.Distance(a, b);
                    if (back <= len || k + 2 == p.overflow.Length)
                    {
                        var d = (b - a) / Mathf.Max(len, 1e-4f);
                        var r = new Vector2(d.y, -d.x);
                        facing = -d;
                        float lat = p.overflowHalfW[k] * 0.55f + side;
                        return a + d * Mathf.Min(back, len) + r * lat;
                    }
                    back -= len;
                }
            }
            facing = p.dir;
            return p.entrance - p.dir * back + right * side;
        }

        /// <summary>Spread queue overflow (people standing on walkways) over the overflow edges.</summary>
        public static void AddStanding(Poi p, int queueCount, float[] standing)
        {
            int over = queueCount - p.queueCap;
            if (over <= 0 || p.overflowEdges == null) return;
            float rem = 0.6f + (over / 2f) * Pitch;
            for (int k = 0; k < p.overflowEdges.Length && rem > 0f; k++)
            {
                float len = Vector2.Distance(p.overflow[k], p.overflow[k + 1]);
                float take = Mathf.Min(rem, len);
                standing[p.overflowEdges[k]] += take / Pitch * 2f;
                rem -= take;
            }
        }
    }

    /// <summary>Deterministic theme-park day: arrivals, choice, walking (density-dependent), queues, rides, food, rest, exits.</summary>
    public class ParkSim
    {
        public const float MaxLoadWait = 30f;
        public readonly ParkLayout L;
        public readonly SimParams P;
        public readonly PathGraph G;
        public readonly Telemetry tel;
        public readonly PoiState[] poi;
        public readonly int[] edgeCount;
        public readonly float[] edgeDensity;
        public readonly float[] edgeStanding;     // queue overflow standing on walkways (persons)
        public float browseChance = 0.5f;
        public readonly List<Visitor> visitors = new List<Visitor>();
        public float t;
        public int inPark, entered, exited;
        public int nWalking, nQueue, nRiding, nEating, nResting, nBrowsing;
        public long steps;

        readonly float[] arrivals;
        int nextArrival;
        readonly System.Random rng;
        float costV0 = 1.3f;
        readonly PathGraph.EdgeCost plainCost, crowdCost;
        float nextSample, nextHeat;
        public const float HeatEvery = 2f;

        public ParkSim(ParkLayout layout, SimParams p)
        {
            L = layout; P = p.Clone();
            G = new PathGraph(L);
            rng = new System.Random(P.seed);
            poi = new PoiState[L.pois.Count];
            for (int i = 0; i < poi.Length; i++) poi[i] = new PoiState { p = L.pois[i] };
            edgeCount = new int[L.edges.Count];
            edgeDensity = new float[L.edges.Count];
            edgeStanding = new float[L.edges.Count];
            plainCost = e => e.length / costV0;
            crowdCost = e => e.length / (costV0 * Walk.SpeedFactor(edgeDensity[e.id]));
            arrivals = new float[Mathf.Max(0, P.dailyVisitors)];
            for (int i = 0; i < arrivals.Length; i++)
            {
                double h;
                if (rng.NextDouble() < 0.75) { do h = Normal(10.0, 0.8); while (h < 9.0 || h > 15.0); }
                else h = 9.0 + rng.NextDouble() * 8.0;
                arrivals[i] = (float)(h * 3600.0);
            }
            Array.Sort(arrivals);
            t = L.openTime;
            nextSample = t; nextHeat = t;
            tel = new Telemetry(this);
        }

        public bool Open => t < L.closeTime;
        public bool Finished => t >= L.closeTime && nextArrival >= arrivals.Length && inPark == 0;
        public int DailyVisitors => arrivals.Length;

        double Normal(double mean, double sd)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }
        float U() => (float)rng.NextDouble();
        float Gumbel() => -Mathf.Log(-Mathf.Log(Mathf.Clamp(U(), 1e-6f, 1f - 1e-6f)));

        // ------------------------------------------------------------------ stepping

        public void Step()
        {
            float dt = P.dt;
            while (nextArrival < arrivals.Length && arrivals[nextArrival] <= t) Spawn(arrivals[nextArrival++]);

            float hungerRate = HungerRate(t) * dt, fatigueRate = dt / (5.5f * 3600f);
            for (int i = 0; i < visitors.Count; i++)
            {
                var v = visitors[i];
                if (v.state == VState.Gone) continue;
                v.hunger += hungerRate; v.fatigue += fatigueRate;
                switch (v.state)
                {
                    case VState.Walking: Move(v, dt); break;
                    case VState.Riding: if (t >= v.until) RideEnd(v); break;
                    case VState.Eating: if (t >= v.until) EatEnd(v); break;
                    case VState.Resting: if (t >= v.until) RestEnd(v); break;
                    case VState.Browsing: if (t >= v.until) Decide(v); break;
                }
            }
            foreach (var st in poi) Serve(st);
            Array.Clear(edgeStanding, 0, edgeStanding.Length);
            foreach (var st in poi) if (st.queue.Count > st.p.queueCap) QueueLayout.AddStanding(st.p, st.queue.Count, edgeStanding);
            for (int e = 0; e < edgeDensity.Length; e++) edgeDensity[e] = (edgeCount[e] + edgeStanding[e]) / Mathf.Max(1f, L.edges[e].Area);

            t += dt; steps++;
            if (t >= nextHeat) { nextHeat += HeatEvery; Count(); tel.AccumulateHeat(HeatEvery); }
            if (t >= nextSample) { nextSample += Telemetry.SampleEvery; tel.Sample(); }
        }

        /// <summary>Advance by <paramref name="seconds"/> of simulated time (bounded by <paramref name="maxSteps"/>).</summary>
        public int Advance(float seconds, int maxSteps = int.MaxValue)
        {
            float end = t + seconds; int n = 0;
            while (t < end && n < maxSteps) { Step(); n++; }
            return n;
        }

        public void RunUntil(float clock) { while (t < clock && !(Finished && t > L.closeTime + 60f)) Step(); }

        static float HungerRate(float t)
        {
            float h = t / 3600f;
            float k = 1f;
            if (h >= 11.5f && h < 13.5f) k = 2.2f;
            else if (h >= 17.5f && h < 19f) k = 1.6f;
            return k / (4.5f * 3600f);
        }

        void Count()
        {
            nWalking = nQueue = nRiding = nEating = nResting = nBrowsing = 0;
            foreach (var v in visitors)
            {
                switch (v.state)
                {
                    case VState.Walking: nWalking++; break;
                    case VState.Queue: nQueue++; break;
                    case VState.Riding: nRiding++; break;
                    case VState.Eating: nEating++; break;
                    case VState.Resting: nResting++; break;
                    case VState.Browsing: nBrowsing++; break;
                }
            }
        }

        public void RecountNow() => Count();

        void Spawn(float at)
        {
            var v = new Visitor { id = visitors.Count, ridden = new byte[L.pois.Count] };
            double r = rng.NextDouble();
            v.type = r < 0.45 ? VType.Family : r < 0.65 ? VType.Teen : VType.Adult;
            v.thrillPref = v.type == VType.Family ? 0.2f + 0.25f * U() : v.type == VType.Teen ? 0.8f + 0.2f * U() : 0.4f + 0.35f * U();
            float k = v.type == VType.Family ? 0.92f : v.type == VType.Teen ? 1.05f : 1f;
            v.v0 = Mathf.Clamp((float)Normal(P.walkSpeed, P.walkSpeedSd) * k, 0.8f, 1.8f);
            v.guided = U() < P.guidedShare;
            v.arriveT = at;
            float stay = Mathf.Clamp((float)Normal(P.stayMeanHours, P.staySdHours), 1.5f, 11f) * 3600f;
            v.leaveT = Mathf.Min(at + stay, L.closeTime);
            v.hunger = 0.3f * U(); v.fatigue = 0.1f * U();
            v.hungerLimit = 0.7f + 0.25f * U(); v.fatigueLimit = 0.8f + 0.3f * U();
            v.lateral = -0.15f + 1.05f * U();
            v.node = L.gateNode;
            v.pos = L.nodes[L.gateNode].pos;
            v.state = VState.Walking;
            visitors.Add(v);
            inPark++; entered++;
            tel.Log(EvType.Arrive, v, -1);
            Decide(v);
        }

        // ------------------------------------------------------------------ decisions

        void RunCosts(Visitor v, int from)
        {
            costV0 = v.v0;
            G.Run(from, v.guided ? crowdCost : plainCost);
        }

        void Decide(Visitor v)
        {
            v.state = VState.Walking;
            v.edge = -1;
            RunCosts(v, v.node);
            if (t >= v.leaveT || !Open) { GoTo(v, Goal.Exit, -1); return; }

            if (v.hunger >= v.hungerLimit)
            {
                int f = BestFood(v);
                // everything packed (> 25 min wait)? postpone the meal and do something else first
                if (f >= 0 && poi[f].WaitNow(t) > 25f * 60f && v.hunger < v.hungerLimit + 0.35f) { v.hungerLimit += 0.12f; f = -1; }
                if (f >= 0) { GoTo(v, Goal.Food, f); return; }
            }
            if (v.fatigue >= v.fatigueLimit)
            {
                int best = -1; float bd = float.PositiveInfinity;
                foreach (var p in L.pois)
                {
                    if (p.kind != PoiKind.Rest) continue;
                    float d = G.dist[p.node] * (0.8f + 0.4f * U());
                    if (d < bd) { bd = d; best = p.id; }
                }
                if (best >= 0) { GoTo(v, Goal.Rest, best); return; }
            }
            if (U() < browseChance)
            {
                int n = BrowseNode(v);
                if (n >= 0) { GoTo(v, Goal.Browse, n); return; }
            }
            int ride = BestRide(v);
            if (ride >= 0) GoTo(v, Goal.Ride, ride);
            else GoTo(v, Goal.Exit, -1);
        }

        int BestRide(Visitor v)
        {
            int best = -1; float bestU = float.NegativeInfinity;
            foreach (var p in L.pois)
            {
                if (p.kind != PoiKind.Ride) continue;
                float walk = G.dist[p.node];
                if (float.IsInfinity(walk)) continue;
                var st = poi[p.id];
                float wait = v.guided ? st.WaitForecast(t, walk) : st.WaitNow(t);
                float u = 2.2f * p.popularity + 1.6f * (1f - Mathf.Abs(p.thrill - v.thrillPref))
                          - 0.10f * walk / 60f - 0.05f * wait / 60f - 1.4f * v.ridden[p.id];
                if (v.type == VType.Family) { if (p.kidsRide) u += 1.2f; if (p.tallOnly) u -= 2.5f; }
                else if (p.kidsRide) u -= 2.5f;
                if (p.id == v.lastPoi) u -= 3f;
                if (t + walk + wait + p.duration > v.leaveT + 1800f) u -= 2f;
                u += 0.6f * Gumbel();
                if (u > bestU) { bestU = u; best = p.id; }
            }
            return best;
        }

        /// <summary>Stroll: a random walkway junction 40-240 s away (shops, photo spots, just looking around).</summary>
        int BrowseNode(Visitor v)
        {
            int best = -1, seen = 0;
            foreach (var n in L.nodes)
            {
                if (n.id == v.node || n.id == L.gateNode || n.name[0] == '@') continue;
                float d = G.dist[n.id];
                if (d < 40f || d > 240f) continue;
                seen++;
                if (rng.Next(seen) == 0) best = n.id;
            }
            return best;
        }

        int BestFood(Visitor v)
        {
            int best = -1; float bc = float.PositiveInfinity;
            foreach (var p in L.pois)
            {
                if (p.kind != PoiKind.Food) continue;
                float walk = G.dist[p.node];
                if (float.IsInfinity(walk)) continue;
                float c = walk + poi[p.id].WaitNow(t) + 120f * Gumbel();
                if (c < bc) { bc = c; best = p.id; }
            }
            return best;
        }

        /// <summary>Walking time along edges using the current densities.</summary>
        public float RouteTime(List<int> edges, float v0, float startCut = 0f)
        {
            float s = 0f;
            for (int i = 0; i < edges.Count; i++)
            {
                var e = L.edges[edges[i]];
                float len = i == 0 ? Mathf.Max(0f, e.length - startCut) : e.length;
                s += len / (v0 * Walk.SpeedFactor(edgeDensity[e.id]));
            }
            return s;
        }

        void GoTo(Visitor v, Goal goal, int target)
        {
            v.goal = goal; v.target = target;
            int tn = goal == Goal.Exit ? L.gateNode : goal == Goal.Browse ? target : L.pois[target].node;
            G.PathTo(tn, v.route, v.routeEdges);
            v.tripStart = t;
            v.trip = -1;
            if (goal == Goal.Ride)
            {
                var st = poi[target];
                float predWalk = RouteTime(v.routeEdges, v.v0);
                v.trip = tel.StartTrip(v, target, predWalk, st.WaitNow(t), st.WaitForecast(t, predWalk));
            }
            if (v.routeEdges.Count == 0) { v.node = tn; Arrive(v); return; }
            v.leg = 0; v.s = 0f;
            v.edge = v.routeEdges[0];
            edgeCount[v.edge]++;
            UpdatePos(v);
        }

        // ------------------------------------------------------------------ movement

        void Move(Visitor v, float dt)
        {
            if (v.edge < 0) return;
            var e = L.edges[v.edge];
            float step = v.v0 * Walk.SpeedFactor(edgeDensity[e.id]) * dt;
            v.s += step; v.distance += step; v.fatigue += step / 9000f;
            while (v.s >= e.length)
            {
                v.s -= e.length;
                edgeCount[e.id]--;
                tel.EdgeTraversed(e.id);
                v.leg++;
                if (v.leg >= v.routeEdges.Count)
                {
                    v.node = v.route[v.route.Count - 1];
                    v.edge = -1;
                    v.pos = L.nodes[v.node].pos;
                    Arrive(v);
                    return;
                }
                e = L.edges[v.routeEdges[v.leg]];
                v.edge = e.id;
                edgeCount[e.id]++;
            }
            UpdatePos(v);
        }

        void UpdatePos(Visitor v)
        {
            var e = L.edges[v.edge];
            Vector2 a = L.nodes[v.route[v.leg]].pos, b = L.nodes[v.route[v.leg + 1]].pos;
            var d = (b - a) / Mathf.Max(e.length, 1e-4f);
            var right = new Vector2(d.y, -d.x);
            v.heading = d;
            v.pos = a + d * v.s + right * (v.lateral * (e.width * 0.5f - 0.4f));
        }

        // ------------------------------------------------------------------ arrivals at POIs

        void Arrive(Visitor v)
        {
            if (v.goal == Goal.Exit)
            {
                v.state = VState.Gone;
                inPark--; exited++;
                tel.Exited(v);
                return;
            }
            if (v.goal == Goal.Browse)
            {
                // linger at a shop front / photo spot next to the junction
                v.state = VState.Browsing;
                v.until = t + Mathf.Lerp(4f, 18f, U()) * 60f;
                float r = 2f;
                foreach (var ei in L.nodes[v.node].edges) r = Mathf.Max(r, L.edges[ei].width * 0.5f + 1.2f);
                float a = U() * Mathf.PI * 2f;
                v.spot = L.nodes[v.node].pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + U() * 1.5f);
                v.heading = (L.nodes[v.node].pos - v.spot).normalized;
                return;
            }
            var p = L.pois[v.target];
            var st = poi[p.id];
            tel.Arrived(v, t - v.tripStart);
            switch (p.kind)
            {
                case PoiKind.Ride:
                    if (!Open) { Decide(v); return; }
                    Join(st, v);
                    break;
                case PoiKind.Food:
                    if (!Open) { Decide(v); return; }
                    if (st.inside < p.capacity && st.queue.Count == 0) StartEat(st, v);
                    else Join(st, v);
                    break;
                case PoiKind.Rest:
                    v.state = VState.Resting;
                    v.until = t + Mathf.Lerp(p.serviceMin, p.serviceMax, U());
                    v.spot = p.Local((U() - 0.5f) * (p.size.x - 1.2f), (U() - 0.5f) * (p.size.y - 1.2f));
                    st.inside++; st.riders++;
                    tel.Log(EvType.RestStart, v, p.id);
                    break;
                default:
                    Decide(v);
                    break;
            }
        }

        void Join(PoiState st, Visitor v)
        {
            if (st.p.kind == PoiKind.Ride && st.queue.Count == 0 && t >= st.nextDispatch)
                st.nextDispatch = Mathf.Max(st.lastDispatch + st.p.interval, t + st.LoadWait);
            st.queue.Add(v.id);
            st.joined++; st.joinedWindow++;
            v.state = VState.Queue;
            v.queueStart = t;
            v.heading = st.p.dir;
            tel.Log(EvType.Join, v, st.p.id);
        }

        void Serve(PoiState st)
        {
            var p = st.p;
            if (p.kind == PoiKind.Ride)
            {
                if (st.queue.Count == 0 || t < st.nextDispatch) return;
                int n = Mathf.Min(p.batch, st.queue.Count);
                for (int i = 0; i < n; i++) Board(st, visitors[st.queue[i]]);
                st.queue.RemoveRange(0, n);
                st.lastDispatch = t;
                st.nextDispatch = t + p.interval;
                tel.Dispatched(p.id, n);
            }
            else if (p.kind == PoiKind.Food)
            {
                int n = 0;
                while (n < st.queue.Count && st.inside < p.capacity) StartEat(st, visitors[st.queue[n++]]);
                if (n > 0) st.queue.RemoveRange(0, n);
            }
        }

        void Board(PoiState st, Visitor v)
        {
            float wait = t - v.queueStart;
            st.waitSum += wait; st.waitN++; st.waitMax = Mathf.Max(st.waitMax, wait);
            st.riders++; st.riding++;
            v.state = VState.Riding;
            v.until = t + st.p.duration;
            v.rides++;
            v.ridden[st.p.id]++;
            v.lastPoi = st.p.id;
            tel.Boarded(v, st.p.id, wait);
        }

        void RideEnd(Visitor v)
        {
            poi[v.target].riding--;
            tel.Log(EvType.RideEnd, v, v.target);
            v.node = L.pois[v.target].node;
            v.pos = L.nodes[v.node].pos;
            Decide(v);
        }

        void StartEat(PoiState st, Visitor v)
        {
            if (v.state == VState.Queue)
            {
                float wait = t - v.queueStart;
                st.waitSum += wait; st.waitN++; st.waitMax = Mathf.Max(st.waitMax, wait);
            }
            else { st.waitN++; }
            st.inside++; st.riders++;
            v.state = VState.Eating;
            v.until = t + Mathf.Lerp(st.p.serviceMin, st.p.serviceMax, U());
            v.spot = st.p.Local((U() - 0.5f) * (st.p.size.x - 1.5f), (U() - 0.5f) * (st.p.size.y - 1.5f));
            v.lastPoi = st.p.id;
            tel.Log(EvType.FoodStart, v, st.p.id);
        }

        void EatEnd(Visitor v)
        {
            var st = poi[v.target];
            st.inside--;
            v.hunger = 0f;
            v.hungerLimit = 0.7f + 0.25f * U();
            v.fatigue = Mathf.Max(0f, v.fatigue - 0.25f);
            tel.Log(EvType.FoodEnd, v, v.target);
            v.node = st.p.node;
            v.pos = L.nodes[v.node].pos;
            Decide(v);
        }

        void RestEnd(Visitor v)
        {
            var st = poi[v.target];
            st.inside--;
            v.fatigue = Mathf.Max(0f, v.fatigue - 0.6f);
            v.lastPoi = st.p.id;
            tel.Log(EvType.RestEnd, v, v.target);
            v.node = st.p.node;
            v.pos = L.nodes[v.node].pos;
            Decide(v);
        }

        // ------------------------------------------------------------------ positions for rendering / heat

        public delegate void VisibleFn(Visitor v, Vector2 pos, Vector2 facing);

        /// <summary>Every visitor that is visible in the park (not riding, not gone) with its position and facing.</summary>
        public void ForEachVisible(VisibleFn fn)
        {
            foreach (var v in visitors)
            {
                switch (v.state)
                {
                    case VState.Walking: fn(v, v.pos, v.heading); break;
                    case VState.Eating:
                    case VState.Resting:
                    case VState.Browsing: fn(v, v.spot, v.heading); break;
                }
            }
            foreach (var st in poi)
            {
                for (int i = 0; i < st.queue.Count; i++)
                {
                    var p = QueueLayout.Slot(st.p, i, out var f);
                    fn(visitors[st.queue[i]], p, f);
                }
            }
        }

        public Vector2 PositionOf(Visitor v)
        {
            if (v.state == VState.Queue)
            {
                var st = poi[v.target];
                int i = st.queue.IndexOf(v.id);
                if (i >= 0) return QueueLayout.Slot(st.p, i, out _);
            }
            if (v.state == VState.Eating || v.state == VState.Resting || v.state == VState.Browsing) return v.spot;
            if (v.state == VState.Riding) return L.pois[v.target].pos;
            return v.pos;
        }

        public Visitor NearestVisitor(Vector2 p, float maxDist)
        {
            Visitor best = null; float bd = maxDist * maxDist;
            ForEachVisible((v, pos, f) =>
            {
                float d = (pos - p).sqrMagnitude;
                if (d < bd) { bd = d; best = v; }
            });
            return best;
        }
    }
}
