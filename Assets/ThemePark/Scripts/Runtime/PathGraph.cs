using System.Collections.Generic;
using UnityEngine;

namespace ThemePark
{
    /// <summary>Pedestrian walking model: speed falls with density (Weidmann 1993) and Fruin walkway levels of service.</summary>
    public static class Walk
    {
        public const float FreeSpeed = 1.3f;     // m/s, guide default
        public const float JamDensity = 5.4f;    // persons/m²
        public static readonly float[] LosLimits = { 0.31f, 0.43f, 0.72f, 1.08f, 2.17f };   // A|B|C|D|E|F (persons/m²)
        public static readonly string[] LosNames = { "A", "B", "C", "D", "E", "F" };

        /// <summary>Fraction of free speed at density <paramref name="rho"/> (persons/m²).</summary>
        public static float SpeedFactor(float rho)
        {
            if (rho <= 0.05f) return 1f;
            float f = 1f - Mathf.Exp(-1.913f * (1f / rho - 1f / JamDensity));
            return Mathf.Clamp(f, 0.08f, 1f);
        }

        public static int Los(float rho)
        {
            for (int i = 0; i < LosLimits.Length; i++) if (rho < LosLimits[i]) return i;
            return 5;
        }

        public static readonly Color[] LosColors =
        {
            new Color(0.20f, 0.75f, 0.35f), new Color(0.55f, 0.85f, 0.25f), new Color(0.98f, 0.85f, 0.20f),
            new Color(1.00f, 0.55f, 0.10f), new Color(0.95f, 0.25f, 0.15f), new Color(0.65f, 0.05f, 0.25f)
        };
    }

    /// <summary>Walkway graph with a binary-heap Dijkstra (multi-source, pluggable edge cost).</summary>
    public class PathGraph
    {
        public readonly ParkLayout L;
        public readonly float[] dist;
        public readonly int[] prevNode, prevEdge;
        readonly int[] heapNode;
        readonly float[] heapKey;
        int heapCount;

        public PathGraph(ParkLayout layout)
        {
            L = layout;
            int n = L.nodes.Count;
            dist = new float[n]; prevNode = new int[n]; prevEdge = new int[n];
            heapNode = new int[L.edges.Count * 2 + n + 8];
            heapKey = new float[heapNode.Length];
        }

        public delegate float EdgeCost(PathEdge e);

        public void Run(int source, EdgeCost cost) { Run(new[] { source }, new[] { 0f }, cost); }

        public void Run(int[] sources, float[] initial, EdgeCost cost)
        {
            int n = dist.Length;
            for (int i = 0; i < n; i++) { dist[i] = float.PositiveInfinity; prevNode[i] = -1; prevEdge[i] = -1; }
            heapCount = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                if (initial[i] < dist[sources[i]]) { dist[sources[i]] = initial[i]; Push(sources[i], initial[i]); }
            }
            while (heapCount > 0)
            {
                Pop(out int u, out float du);
                if (du > dist[u]) continue;
                foreach (int ei in L.nodes[u].edges)
                {
                    var e = L.edges[ei];
                    int v = e.Other(u);
                    float nd = du + cost(e);
                    if (nd < dist[v]) { dist[v] = nd; prevNode[v] = u; prevEdge[v] = ei; Push(v, nd); }
                }
            }
        }

        /// <summary>Node sequence from the source to <paramref name="target"/> (after <see cref="Run"/>).</summary>
        public bool PathTo(int target, List<int> nodes, List<int> edges)
        {
            nodes.Clear(); edges?.Clear();
            if (float.IsInfinity(dist[target])) return false;
            for (int v = target; v >= 0; v = prevNode[v])
            {
                nodes.Add(v);
                if (edges != null && prevEdge[v] >= 0) edges.Add(prevEdge[v]);
            }
            nodes.Reverse();
            edges?.Reverse();
            return true;
        }

        void Push(int node, float key)
        {
            int i = heapCount++;
            heapNode[i] = node; heapKey[i] = key;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (heapKey[p] <= heapKey[i]) break;
                Swap(i, p); i = p;
            }
        }

        void Pop(out int node, out float key)
        {
            node = heapNode[0]; key = heapKey[0];
            heapCount--;
            if (heapCount == 0) return;
            heapNode[0] = heapNode[heapCount]; heapKey[0] = heapKey[heapCount];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < heapCount && heapKey[l] < heapKey[m]) m = l;
                if (r < heapCount && heapKey[r] < heapKey[m]) m = r;
                if (m == i) break;
                Swap(i, m); i = m;
            }
        }

        void Swap(int a, int b)
        {
            (heapNode[a], heapNode[b]) = (heapNode[b], heapNode[a]);
            (heapKey[a], heapKey[b]) = (heapKey[b], heapKey[a]);
        }

        public PathEdge EdgeBetween(int a, int b)
        {
            foreach (int ei in L.nodes[a].edges) if (L.edges[ei].Other(a) == b) return L.edges[ei];
            return null;
        }

        /// <summary>Closest point on any walkway to <paramref name="p"/>.</summary>
        public (int edge, float t, Vector2 point, float dist) Nearest(Vector2 p)
        {
            int best = -1; float bestD = float.PositiveInfinity, bestT = 0f; Vector2 bestP = p;
            foreach (var e in L.edges)
            {
                Vector2 a = L.nodes[e.a].pos, b = L.nodes[e.b].pos, ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                var q = a + ab * t;
                float d = (p - q).sqrMagnitude;
                if (d < bestD) { bestD = d; best = e.id; bestT = t; bestP = q; }
            }
            return (best, bestT, bestP, Mathf.Sqrt(bestD));
        }

        public int NearestNode(Vector2 p)
        {
            int best = 0; float bd = float.PositiveInfinity;
            foreach (var n in L.nodes) { float d = (n.pos - p).sqrMagnitude; if (d < bd) { bd = d; best = n.id; } }
            return best;
        }
    }
}
