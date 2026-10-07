using System.Collections.Generic;
using UnityEngine;

namespace ThemePark
{
    /// <summary>Builds the park scenery from a <see cref="ParkLayout"/>: ground, walkways, lakes, trees, fence, gate, lamps,
    /// queue rails and every POI model.</summary>
    public static class ParkBuilder
    {
        public static readonly Vector2[] Lakes = { new Vector2(-30f, 40f), new Vector2(22f, -60f) };
        public static readonly float[] LakeRadius = { 11f, 8f };
        public static readonly Vector2 Fountain = new Vector2(0f, 8f);

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public static List<RideAnimator> Build(ParkLayout L, Transform root, IMaterialProvider mats, IMeshStore store)
        {
            Prims.ClearChildren(root);
            var anims = new List<RideAnimator>();
            Ground(L, root, mats);
            Paths(L, root, mats, store);
            Water(L, root, mats, store);
            Trees(L, root, mats, store);
            Fence(L, root, mats, store);
            Gate(L, root, mats);
            Lamps(L, root, mats, store);
            QueueRails(L, root, mats, store);
            var pois = Prims.Node("Attractions", root, Vector3.zero);
            foreach (var p in L.pois)
                if (p.kind != PoiKind.Gate) anims.Add(RideBuilders.Build(p, pois, mats, store));
            return anims;
        }

        // ------------------------------------------------------------------ ground & paths

        static void Ground(ParkLayout L, Transform root, IMaterialProvider mats)
        {
            Prims.Box("Ground", root, new Vector3(0f, -0.5f, 0f), new Vector3(900f, 1f, 900f), mats.Get("GrassOutside", Hex("#7da35a"), 0.1f), shadows: false);
            var b = L.bounds;
            Prims.Box("ParkLawn", root, new Vector3(b.center.x, 0.005f, (b.yMax + L.fenceZ) * 0.5f), new Vector3(b.width, 0.01f, b.yMax - L.fenceZ),
                mats.Get("Grass", Hex("#8cbf5f"), 0.1f), shadows: false);
            Prims.Box("GatePlaza", root, new Vector3(0f, 0.01f, (L.fenceZ + b.yMin) * 0.5f - 1f), new Vector3(60f, 0.02f, L.fenceZ - b.yMin + 6f),
                mats.Get("PlazaStone", Hex("#d8d2c4")), shadows: false);
        }

        static void AddQuad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });   // clockwise seen from above
        }

        static void AddDisc(List<Vector3> v, List<int> t, Vector3 c, float r, int seg = 20)
        {
            int k = v.Count; v.Add(c);
            for (int i = 0; i <= seg; i++) { float a = -i * Mathf.PI * 2f / seg; v.Add(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)); }
            for (int i = 0; i < seg; i++) t.AddRange(new[] { k, k + i + 1, k + i + 2 });
        }

        static void Ribbon(List<Vector3> v, List<int> t, Vector2 a, Vector2 b, float w, float y)
        {
            var d = (b - a).normalized; var r = new Vector2(d.y, -d.x) * (w * 0.5f);
            AddQuad(v, t, V(a - r, y), V(a + r, y), V(b + r, y), V(b - r, y));
        }

        static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static Mesh Flat(List<Vector3> v, List<int> t)
        {
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(t, 0);
            var n = new Vector3[v.Count]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        static float NodeRadius(ParkLayout L, PathNode n)
        {
            float w = 0f; foreach (var e in n.edges) w = Mathf.Max(w, L.edges[e].width);
            return w * 0.5f;
        }

        static void Paths(ParkLayout L, Transform root, IMaterialProvider mats, IMeshStore store)
        {
            var parent = Prims.Node("Walkways", root, Vector3.zero);
            // curb outline under everything
            var cv = new List<Vector3>(); var ct = new List<int>();
            foreach (var e in L.edges) Ribbon(cv, ct, L.nodes[e.a].pos, L.nodes[e.b].pos, e.width + 0.7f, 0.02f);
            foreach (var n in L.nodes) AddDisc(cv, ct, V(n.pos, 0.02f), NodeRadius(L, n) + 0.35f);
            Prims.MeshObject("Curb", parent, store.Save("Path_Curb", Flat(cv, ct)), mats.Get("Curb", Hex("#8d8a84"), 0.2f), false);
            for (int z = 0; z < L.zones.Count; z++)
            {
                var v = new List<Vector3>(); var t = new List<int>();
                float y = 0.03f + 0.003f * z;
                foreach (var e in L.edges) if (e.zone == z) Ribbon(v, t, L.nodes[e.a].pos, L.nodes[e.b].pos, e.width, y);
                foreach (var n in L.nodes) if (n.zone == z) AddDisc(v, t, V(n.pos, y + 0.001f), NodeRadius(L, n));
                if (v.Count == 0) continue;
                var zone = L.zones[z];
                Prims.MeshObject("Path_" + zone.name, parent, store.Save("Path_Zone" + z, Flat(v, t)),
                    mats.Get("Pave_" + z, zone.color, 0.15f), false);
            }
            // hub plaza paving inside the ring
            var pv = new List<Vector3>(); var pt = new List<int>();
            AddDisc(pv, pt, new Vector3(0f, 0.025f, 0f), 21f, 40);
            Prims.MeshObject("HubPlaza", parent, store.Save("Path_Hub", Flat(pv, pt)), mats.Get("HubStone", Hex("#e3dccd"), 0.15f), false);
        }

        static void Water(ParkLayout L, Transform root, IMaterialProvider mats, IMeshStore store)
        {
            var parent = Prims.Node("Water", root, Vector3.zero);
            var cyl = store.Save("Cyl24", ProcMesh.Cylinder(24));
            var water = mats.Get("Water", new Color(0.18f, 0.52f, 0.78f), 0.92f);
            var sand = mats.Get("Shore", Hex("#d9c79a"), 0.1f);
            for (int i = 0; i < Lakes.Length; i++)
            {
                var s = Prims.MeshObject("Shore" + i, parent, cyl, sand, false);
                s.transform.localPosition = V(Lakes[i], 0f);
                s.transform.localScale = new Vector3((LakeRadius[i] + 1.5f) * 2f, 0.04f, (LakeRadius[i] + 1.5f) * 2f);
                var w = Prims.MeshObject("Lake" + i, parent, cyl, water, false);
                w.transform.localPosition = V(Lakes[i], 0.01f);
                w.transform.localScale = new Vector3(LakeRadius[i] * 2f, 0.05f, LakeRadius[i] * 2f);
            }
            var stone = mats.Get("Stone", Hex("#c9c3b6"), 0.2f);
            var f = Prims.MeshObject("FountainBasin", parent, cyl, stone);
            f.transform.localPosition = V(Fountain, 0f); f.transform.localScale = new Vector3(8f, 0.6f, 8f);
            var fw = Prims.MeshObject("FountainWater", parent, cyl, water, false);
            fw.transform.localPosition = V(Fountain, 0.6f); fw.transform.localScale = new Vector3(7.2f, 0.02f, 7.2f);
            var c = Prims.MeshObject("FountainColumn", parent, cyl, stone);
            c.transform.localPosition = V(Fountain, 0f); c.transform.localScale = new Vector3(0.8f, 2.6f, 0.8f);
            var top = Prims.MeshObject("FountainBowl", parent, cyl, stone);
            top.transform.localPosition = V(Fountain, 2.4f); top.transform.localScale = new Vector3(2.6f, 0.3f, 2.6f);
            var jet = Prims.MeshObject("FountainJet", parent, cyl, mats.Get("WaterJet", new Color(0.75f, 0.90f, 1f), 0.95f), false);
            jet.transform.localPosition = V(Fountain, 2.7f); jet.transform.localScale = new Vector3(0.35f, 1.6f, 0.35f);
        }

        // ------------------------------------------------------------------ trees

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>True when <paramref name="p"/> is free for decoration (off walkways, POIs, water, plazas).</summary>
        public static bool Free(ParkLayout L, Vector2 p, float margin)
        {
            var b = L.bounds;
            if (p.x < b.xMin + 3f || p.x > b.xMax - 3f || p.y > b.yMax - 3f || p.y < L.fenceZ + 3f) return false;
            foreach (var e in L.edges) if (SegDist(p, L.nodes[e.a].pos, L.nodes[e.b].pos) < e.width * 0.5f + margin) return false;
            foreach (var n in L.nodes) if (Vector2.Distance(p, n.pos) < 3f + margin) return false;
            foreach (var poi in L.pois)
            {
                if (poi.kind == PoiKind.Gate) continue;
                if (poi.Contains(p, margin + 1f)) return false;
                var qc = poi.entrance + poi.dir * (poi.queueDepth * 0.5f);
                var right = new Vector2(poi.dir.y, -poi.dir.x);
                var d = p - qc;
                if (Mathf.Abs(Vector2.Dot(d, right)) < poi.QueueWidth * 0.5f + margin && Mathf.Abs(Vector2.Dot(d, poi.dir)) < poi.queueDepth * 0.5f + margin) return false;
            }
            for (int i = 0; i < Lakes.Length; i++) if (Vector2.Distance(p, Lakes[i]) < LakeRadius[i] + 2f + margin) return false;
            if (p.magnitude < 22f + margin) return false;
            return true;
        }

        static void Trees(ParkLayout L, Transform root, IMaterialProvider mats, IMeshStore store)
        {
            var rng = new System.Random(7);
            var b = new MeshBatch();
            var trunk = ProcMesh.Cylinder(6); var crown = ProcMesh.Sphere(8, 5); var pine = ProcMesh.Cone(8);
            int count = 0;
            const float step = 7f;
            for (float x = L.bounds.xMin; x < L.bounds.xMax; x += step)
                for (float z = L.fenceZ; z < L.bounds.yMax; z += step)
                {
                    var p = new Vector2(x + (float)rng.NextDouble() * step, z + (float)rng.NextDouble() * step);
                    if (rng.NextDouble() > 0.6) continue;
                    if (!Free(L, p, 2.2f)) continue;
                    float s = 0.8f + 0.5f * (float)rng.NextDouble();
                    bool isPine = rng.NextDouble() < 0.3;
                    b.Add(trunk, Matrix4x4.TRS(V(p, 0f), Quaternion.identity, new Vector3(0.45f * s, 2.4f * s, 0.45f * s)), 0);
                    if (isPine) b.Add(pine, Matrix4x4.TRS(V(p, 1.4f * s), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), new Vector3(3.4f * s, 6f * s, 3.4f * s)), 2);
                    else b.Add(crown, Matrix4x4.TRS(V(p, 3.3f * s), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), new Vector3(4.2f * s, 3.8f * s, 4.2f * s)), rng.NextDouble() < 0.5 ? 1 : 3);
                    count++;
                }
            var parent = Prims.Node("Trees", root, Vector3.zero);
            b.Emit("Trees", parent, new[]
            {
                mats.Get("Trunk", Hex("#6b4a2b"), 0.1f), mats.Get("Leaves", Hex("#4f9a3a"), 0.1f),
                mats.Get("Pine", Hex("#2f6f3a"), 0.1f), mats.Get("Leaves2", Hex("#6db44b"), 0.1f)
            }, store);
            parent.name = $"Trees ({count})";
        }

        // ------------------------------------------------------------------ fence, gate, lamps

        static void Fence(ParkLayout L, Transform root, IMaterialProvider mats, IMeshStore store)
        {
            var b = new MeshBatch();
            float x0 = L.bounds.xMin + 1f, x1 = L.bounds.xMax - 1f, z0 = L.fenceZ, z1 = L.bounds.yMax - 1f;
            var corners = new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i]; var c = corners[(i + 1) % 4];
                float len = Vector2.Distance(a, c);
                int posts = Mathf.CeilToInt(len / 4f);
                for (int k = 0; k <= posts; k++)
                {
                    var p = Vector2.Lerp(a, c, (float)k / posts);
                    if (i == 0 && Mathf.Abs(p.x) < 13f) continue;
                    b.Box(V(p, 0.9f), new Vector3(0.12f, 1.8f, 0.12f), 0);
                }
                // rails (split around the gate on the south side)
                if (i == 0)
                {
                    foreach (var seg in new[] { (x0, -13f), (13f, x1) })
                        foreach (float h in new[] { 0.5f, 1.6f })
                            b.Box(Prims.Beam(new Vector3(seg.Item1, h, z0), new Vector3(seg.Item2, h, z0), 0.06f, 0.08f), 0);
                }
                else foreach (float h in new[] { 0.5f, 1.6f }) b.Box(Prims.Beam(V(a, h), V(c, h), 0.06f, 0.08f), 0);
            }
            b.Emit("Fence", Prims.Node("Fence", root, Vector3.zero), new[] { mats.Get("FenceGreen", Hex("#2f4f3a"), 0.4f, 0.5f) }, store);
        }

        static void Gate(ParkLayout L, Transform root, IMaterialProvider mats)
        {
            var g = L.Gate;
            var gate = Prims.Node("Main Gate", root, new Vector3(0f, 0f, L.fenceZ));
            var red = mats.Get("GateRed", g != null ? g.color : Color.red, 0.4f);
            var cream = mats.Get("GateCream", Hex("#f4ead5"), 0.3f);
            var gold = mats.Get("Gold", new Color(0.92f, 0.74f, 0.25f), 0.7f, 0.8f);
            foreach (var s in new[] { -1f, 1f })
            {
                Prims.Box("Tower", gate, new Vector3(s * 10f, 5f, 0f), new Vector3(4f, 10f, 4f), cream);
                Prims.Box("TowerTop", gate, new Vector3(s * 10f, 10.4f, 0f), new Vector3(4.6f, 0.8f, 4.6f), red);
                var cone = Prims.MeshObject("TowerRoof", gate, ProcMesh.Cone(12), red);
                cone.transform.localPosition = new Vector3(s * 10f, 10.8f, 0f); cone.transform.localScale = new Vector3(4.6f, 4f, 4.6f);
                Prims.Box("Flag", gate, new Vector3(s * 10f + 0.6f, 16.2f, 0f), new Vector3(1.2f, 0.7f, 0.05f), gold);
                Prims.Box("FlagPole", gate, new Vector3(s * 10f, 15.6f, 0f), new Vector3(0.08f, 2f, 0.08f), cream);
                Prims.Box("Booth", gate, new Vector3(s * 16f, 1.4f, -3f), new Vector3(3f, 2.8f, 2.4f), cream);
                Prims.Box("BoothRoof", gate, new Vector3(s * 16f, 3f, -3f), new Vector3(3.6f, 0.3f, 3f), red);
            }
            Prims.Box("Arch", gate, new Vector3(0f, 9f, 0f), new Vector3(16f, 1.6f, 2.4f), red);
            Prims.Box("Sign", gate, new Vector3(0f, 9f, -1.25f), new Vector3(12f, 1.2f, 0.1f), gold, shadows: false);
            for (int i = -2; i <= 2; i++)
                if (i != 0) Prims.Box("Turnstile", gate, new Vector3(i * 2.4f, 0.5f, -1f), new Vector3(0.4f, 1f, 1.4f), mats.Get("Steel", new Color(0.62f, 0.64f, 0.68f), 0.5f, 0.6f));
        }

        static void Lamps(ParkLayout L, Transform root, IMaterialProvider mats, IMeshStore store)
        {
            var b = new MeshBatch();
            var cyl = ProcMesh.Cylinder(6); var sph = ProcMesh.Sphere(6, 4);
            foreach (var e in L.edges)
            {
                if (e.width < 5f) continue;
                Vector2 a = L.nodes[e.a].pos, c = L.nodes[e.b].pos;
                var d = (c - a).normalized; var r = new Vector2(d.y, -d.x) * (e.width * 0.5f + 0.6f);
                int n = Mathf.FloorToInt(e.length / 14f);
                for (int k = 1; k <= n; k++)
                {
                    var p = Vector2.Lerp(a, c, (float)k / (n + 1));
                    foreach (var s in new[] { 1f, -1f })
                    {
                        var q = p + r * s;
                        b.Add(cyl, Matrix4x4.TRS(V(q, 0f), Quaternion.identity, new Vector3(0.14f, 3.6f, 0.14f)), 0);
                        b.Add(sph, Matrix4x4.TRS(V(q, 3.75f), Quaternion.identity, new Vector3(0.45f, 0.45f, 0.45f)), 1);
                    }
                }
            }
            b.Emit("Lamps", Prims.Node("Lamps", root, Vector3.zero), new[] { mats.Get("LampPole", Hex("#2b2f36"), 0.4f, 0.6f), mats.Unlit("LampGlobe", Hex("#fff3c4")) }, store, true);
        }

        static void QueueRails(ParkLayout L, Transform root, IMaterialProvider mats, IMeshStore store)
        {
            var b = new MeshBatch();
            foreach (var p in L.pois)
            {
                if ((p.kind != PoiKind.Ride && p.kind != PoiKind.Food) || p.queueDepth < 2f) continue;
                var right = new Vector2(p.dir.y, -p.dir.x);
                float w = p.QueueWidth;
                Vector3 W(float x, float y, float h) { var q = p.entrance + right * x + p.dir * y; return new Vector3(q.x, h, q.y); }
                int lanes = Mathf.Max(1, Mathf.FloorToInt((p.queueDepth - 0.6f) / QueueLayout.Lane));
                // side rails
                b.Box(Prims.Beam(W(-w * 0.5f, 0.3f, 0.9f), W(-w * 0.5f, p.queueDepth, 0.9f), 0.06f, 0.06f), 0);
                b.Box(Prims.Beam(W(w * 0.5f, 0.3f, 0.9f), W(w * 0.5f, p.queueDepth, 0.9f), 0.06f, 0.06f), 0);
                for (int l = 0; l < lanes - 1; l++)
                {
                    float y = p.queueDepth - 0.4f - (l + 0.5f) * QueueLayout.Lane;
                    bool even = (l & 1) == 0;
                    float xa = even ? -w * 0.5f : -w * 0.5f + 1.0f, xb = even ? w * 0.5f - 1.0f : w * 0.5f;
                    b.Box(Prims.Beam(W(xa, y, 0.9f), W(xb, y, 0.9f), 0.05f, 0.05f), 0);
                    b.Box(Matrix4x4.TRS(W(xa, y, 0.45f), Quaternion.identity, new Vector3(0.06f, 0.9f, 0.06f)), 1);
                    b.Box(Matrix4x4.TRS(W(xb, y, 0.45f), Quaternion.identity, new Vector3(0.06f, 0.9f, 0.06f)), 1);
                }
                // queue floor
                var c = p.entrance + p.dir * (p.queueDepth * 0.5f);
                b.Box(Matrix4x4.TRS(new Vector3(c.x, 0.035f, c.y), Quaternion.Euler(0f, p.Yaw, 0f), new Vector3(w + 0.4f, 0.05f, p.queueDepth)), 2);
            }
            b.Emit("QueueRails", Prims.Node("Queues", root, Vector3.zero), new[]
            {
                mats.Get("QueueRail", Hex("#c0392b"), 0.5f, 0.3f), mats.Get("Steel", new Color(0.62f, 0.64f, 0.68f), 0.5f, 0.6f),
                mats.Get("QueueFloor", Hex("#cfc6b5"), 0.1f)
            }, store, false);
        }
    }
}
