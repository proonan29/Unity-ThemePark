using System.Collections.Generic;
using UnityEngine;

namespace ThemePark
{
    public enum RouteMode { Fastest, Shortest }

    /// <summary>Route answer for the guide: geometry, distance, walking time with the current crowding, and the expected wait.</summary>
    public class GuideResult
    {
        public bool ok;
        public RouteMode mode;
        public int target = -1;
        public readonly List<Vector2> points = new List<Vector2>();
        public readonly List<int> edges = new List<int>();
        public float length;          // m
        public float walkTime;        // s, with current densities
        public float freeWalkTime;    // s, empty park
        public float wait;            // s, forecast at arrival
        public float waitNow;         // s, posted wait right now
        public float Total => walkTime + wait;
        public float departClock, arriveClock, boardClock;
        public int worstLos;          // worst walkway level of service along the route
        public float crowdDelay => walkTime - freeWalkTime;
    }

    /// <summary>Shortest / fastest routes from any point of the park to a POI entrance, with ETA.</summary>
    public static class RouteGuide
    {
        static readonly List<int> tmpNodes = new List<int>();
        static readonly List<int> tmpEdges = new List<int>();

        /// <summary>Route from an arbitrary point (snapped to the nearest walkway) to POI <paramref name="poiId"/>.
        /// <paramref name="sim"/> may be null (empty park, no waits).</summary>
        public static GuideResult Query(ParkLayout L, ParkSim sim, Vector2 from, int poiId, RouteMode mode, float speed = Walk.FreeSpeed)
        {
            var r = new GuideResult { mode = mode, target = poiId };
            if (poiId < 0 || poiId >= L.pois.Count) return r;
            var poi = L.pois[poiId];
            var G = sim != null ? sim.G : new PathGraph(L);
            float[] dens = sim != null ? sim.edgeDensity : null;
            float Factor(int e) => dens == null ? 1f : Walk.SpeedFactor(dens[e]);

            var near = G.Nearest(from);
            if (near.edge < 0) return r;
            var se = L.edges[near.edge];
            float la = near.t * se.length, lb = (1f - near.t) * se.length;
            PathGraph.EdgeCost cost = mode == RouteMode.Shortest
                ? (PathGraph.EdgeCost)(e => e.length)
                : e => e.length / (speed * Factor(e.id));
            float fs = mode == RouteMode.Shortest ? 1f : 1f / (speed * Factor(se.id));
            G.Run(new[] { se.a, se.b }, new[] { la * fs, lb * fs }, cost);
            if (!G.PathTo(poi.node, tmpNodes, tmpEdges)) return r;

            // geometry: start point → snapped point → nodes
            r.points.Add(from);
            r.points.Add(near.point);
            float walk = 0f, free = 0f, len = 0f;
            int first = tmpNodes[0];
            float firstLen = first == se.a ? la : lb;
            len += firstLen;
            walk += firstLen / (speed * Factor(se.id));
            free += firstLen / speed;
            int worst = dens == null ? 0 : Walk.Los(dens[se.id]);
            r.edges.Add(se.id);
            foreach (var n in tmpNodes) r.points.Add(L.nodes[n].pos);
            foreach (var ei in tmpEdges)
            {
                var e = L.edges[ei];
                if (ei == se.id) { // walking back along the start edge: undo the partial piece
                    len -= firstLen; walk -= firstLen / (speed * Factor(se.id)); free -= firstLen / speed;
                    float rest = se.length - firstLen;
                    len += rest; walk += rest / (speed * Factor(se.id)); free += rest / speed;
                    continue;
                }
                len += e.length;
                walk += e.length / (speed * Factor(ei));
                free += e.length / speed;
                if (dens != null) worst = Mathf.Max(worst, Walk.Los(dens[ei]));
                r.edges.Add(ei);
            }
            // first point off-path → snap: add that straight distance too
            float off = near.dist;
            len += off; walk += off / speed; free += off / speed;

            r.length = len; r.walkTime = walk; r.freeWalkTime = free; r.worstLos = worst;
            if (sim != null)
            {
                var st = sim.poi[poiId];
                r.wait = poi.kind == PoiKind.Rest ? 0f : st.WaitForecast(sim.t, walk);
                r.waitNow = poi.kind == PoiKind.Rest ? 0f : st.WaitNow(sim.t);
                r.departClock = sim.t;
            }
            else r.departClock = L.openTime;
            r.arriveClock = r.departClock + r.walkTime;
            r.boardClock = r.arriveClock + r.wait;
            r.ok = true;
            return r;
        }
    }
}
