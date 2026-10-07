using System.Collections.Generic;
using UnityEngine;

namespace ThemePark
{
    /// <summary>Procedural low-poly models for every ride, food outlet and rest area (local +Z = away from the queue).</summary>
    public static class RideBuilders
    {
        static IMaterialProvider M;
        static IMeshStore S;

        static Material Lit(string key, Color c, float sm = 0.35f, float met = 0f) => M.Get(key, c, sm, met);
        static Material Accent(Poi p) => M.Get("Accent_" + p.name.Replace(" ", ""), p.color, 0.45f);
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static Material White => Lit("White", new Color(0.93f, 0.93f, 0.90f));
        static Material Steel => Lit("Steel", new Color(0.62f, 0.64f, 0.68f), 0.5f, 0.6f);
        static Material Dark => Lit("DarkMetal", new Color(0.18f, 0.19f, 0.21f), 0.4f, 0.4f);
        static Material Wood => Lit("Wood", new Color(0.52f, 0.34f, 0.18f), 0.25f);
        static Material Water => Lit("Water", new Color(0.18f, 0.52f, 0.78f), 0.92f);
        static Material Gold => Lit("Gold", new Color(0.92f, 0.74f, 0.25f), 0.7f, 0.8f);
        static Material Glow => M.Unlit("WindowGlow", new Color(1f, 0.85f, 0.45f));

        static Mesh Shared(string key, Mesh m) => S.Save(key, m);
        static Mesh ConeMesh => Shared("Cone12", ProcMesh.Cone(12));
        static Mesh CylMesh => Shared("Cyl12", ProcMesh.Cylinder(12));

        static GameObject Cone(string name, Transform parent, Vector3 basePos, float radius, float height, Material m)
        {
            var g = Prims.MeshObject(name, parent, ConeMesh, m);
            g.transform.localPosition = basePos;
            g.transform.localScale = new Vector3(radius * 2f, height, radius * 2f);
            return g;
        }

        public static RideAnimator Build(Poi p, Transform parent, IMaterialProvider mats, IMeshStore store)
        {
            M = mats; S = store;
            var root = Prims.Node(p.name, parent, new Vector3(p.pos.x, 0f, p.pos.y), Quaternion.Euler(0f, p.Yaw, 0f));
            var anim = root.gameObject.AddComponent<RideAnimator>();
            anim.poiId = p.id; anim.model = p.model;
            if (p.kind == PoiKind.Ride)
                Prims.Box("Pad", root, new Vector3(0f, 0.04f, 0f), new Vector3(p.size.x, 0.08f, p.size.y), Lit("RidePad", new Color(0.70f, 0.68f, 0.64f)), shadows: false);
            switch (p.model)
            {
                case RideModel.Coaster: Coaster(p, root, anim); break;
                case RideModel.DropTower: DropTower(p, root, anim); break;
                case RideModel.Swing: Swing(p, root, anim); break;
                case RideModel.LogFlume: LogFlume(p, root, anim); break;
                case RideModel.PirateShip: PirateShip(p, root, anim); break;
                case RideModel.DarkRide: DarkRide(p, root, anim); break;
                case RideModel.FerrisWheel: FerrisWheel(p, root, anim); break;
                case RideModel.Carousel: Carousel(p, root, anim); break;
                case RideModel.Teacups: Teacups(p, root, anim); break;
                case RideModel.BumperCars: BumperCars(p, root, anim); break;
                case RideModel.KiddieTrain: KiddieTrain(p, root, anim); break;
                case RideModel.MiniCoaster: MiniCoaster(p, root, anim); break;
                case RideModel.FoodCourt: FoodCourt(p, root); break;
                case RideModel.SnackBar: SnackBar(p, root); break;
                case RideModel.RestArea: RestArea(p, root); break;
            }
            anim.Apply(0f, -1f, Mathf.Max(1f, p.duration), false);
            return anim;
        }

        // ------------------------------------------------------------------ tracks

        static float Profile(float s, float[] keys)
        {
            // keys: s0,y0,s1,y1,... (s ascending, last s = 1), cosine interpolation
            for (int i = 0; i + 3 < keys.Length; i += 2)
            {
                float s0 = keys[i], y0 = keys[i + 1], s1 = keys[i + 2], y1 = keys[i + 3];
                if (s <= s1)
                {
                    float f = (s - s0) / Mathf.Max(s1 - s0, 1e-5f);
                    f = (1f - Mathf.Cos(Mathf.PI * Mathf.Clamp01(f))) * 0.5f;
                    return Mathf.Lerp(y0, y1, f);
                }
            }
            return keys[keys.Length - 1];
        }

        static Vector3[] OvalTrack(int n, float rx, float rz, float wobble, float[] heights)
        {
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float s = (float)i / n;
                float u = -Mathf.PI * 0.5f + s * Mathf.PI * 2f;
                pts[i] = new Vector3(rx * Mathf.Cos(u) + wobble * Mathf.Sin(2f * u), Profile(s, heights), rz * Mathf.Sin(u));
            }
            return pts;
        }

        /// <summary>Rails, ties and support columns along a closed track; one mesh per material.</summary>
        static void TrackMesh(string key, Transform root, Vector3[] pts, Material rail, Material support, float gauge, int supportEvery, float railSize = 0.18f)
        {
            var b = new MeshBatch();
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var c = pts[(i + 1) % n];
                var tan = (c - a).normalized;
                var side = Vector3.Cross(Vector3.up, tan).normalized * (gauge * 0.5f);
                b.Box(Prims.Beam(a + side, c + side, railSize, railSize), 0);
                b.Box(Prims.Beam(a - side, c - side, railSize, railSize), 0);
                if (i % 2 == 0) b.Box(Matrix4x4.TRS(a - Vector3.up * 0.12f, Quaternion.LookRotation(tan), new Vector3(gauge + 0.3f, 0.1f, 0.25f)), 0);
                if (supportEvery > 0 && i % supportEvery == 0 && a.y > 1.2f)
                    b.Box(Matrix4x4.TRS(new Vector3(a.x, (a.y - 0.25f) * 0.5f, a.z), Quaternion.identity, new Vector3(0.3f, a.y - 0.25f, 0.3f)), 1);
            }
            b.Emit(key, root, new[] { rail, support }, S);
        }

        static Transform[] Train(Transform root, int cars, Vector3 size, Material body, Material seat, bool engine = false)
        {
            var list = new Transform[cars];
            for (int k = 0; k < cars; k++)
            {
                var n = Prims.Node("Car" + k, root, Vector3.zero);
                Prims.Box("Body", n, new Vector3(0f, size.y * 0.5f + 0.15f, 0f), size, body);
                if (engine && k == 0)
                {
                    Prims.Box("Cab", n, new Vector3(0f, size.y + 0.55f, -size.z * 0.2f), new Vector3(size.x * 0.9f, 0.8f, size.z * 0.45f), seat);
                    Prims.Cyl("Stack", n, new Vector3(0f, size.y + 0.5f, size.z * 0.32f), 0.15f, 0.7f, Dark);
                }
                else Prims.Box("Seat", n, new Vector3(0f, size.y + 0.2f, -size.z * 0.15f), new Vector3(size.x * 0.8f, 0.35f, 0.25f), seat);
                list[k] = n;
            }
            return list;
        }

        // ------------------------------------------------------------------ rides

        static void Coaster(Poi p, Transform root, RideAnimator a)
        {
            var heights = new[] { 0f, 1.5f, 0.04f, 1.5f, 0.25f, 19f, 0.33f, 2.5f, 0.45f, 12f, 0.55f, 2.5f, 0.66f, 9f, 0.76f, 2.2f, 0.86f, 6f, 0.93f, 1.5f, 1f, 1.5f };
            var pts = OvalTrack(140, p.size.x * 0.5f - 3f, p.size.y * 0.5f - 4f, 2.5f, heights);
            TrackMesh("Track_" + p.id, root, pts, Accent(p), White, 1.1f, 3, 0.2f);
            a.track = pts; a.spacing = 2.3f;
            float zf = -(p.size.y * 0.5f - 4f);
            Prims.Box("Station", root, new Vector3(0f, 0.6f, zf - 2.3f), new Vector3(12f, 1.2f, 3f), Lit("Concrete", new Color(0.75f, 0.74f, 0.72f)));
            Prims.Box("StationRoof", root, new Vector3(0f, 4.2f, zf - 1f), new Vector3(13f, 0.3f, 5.5f), Accent(p));
            for (int i = -1; i <= 1; i += 2)
                Prims.Box("RoofPost", root, new Vector3(i * 6f, 2.1f, zf - 3.5f), new Vector3(0.25f, 4.2f, 0.25f), White);
            a.movers = Train(root, 5, new Vector3(1.4f, 0.8f, 2.0f), Lit("CoasterCar", new Color(0.95f, 0.82f, 0.15f)), Dark);
        }

        static void DropTower(Poi p, Transform root, RideAnimator a)
        {
            Prims.Cyl("Base", root, new Vector3(0f, 0.3f, 0f), 5f, 0.6f, Lit("Concrete", new Color(0.75f, 0.74f, 0.72f)));
            Prims.Cyl("Tower", root, new Vector3(0f, 23.5f, 0f), 1.0f, 46f, White);
            Prims.Cyl("Top", root, new Vector3(0f, 47f, 0f), 1.8f, 2f, Accent(p));
            Cone("Spire", root, new Vector3(0f, 48f, 0f), 1.2f, 3f, Accent(p));
            var lift = Prims.Node("Gondola", root, new Vector3(0f, 1.2f, 0f));
            Prims.Cyl("Ring", lift, Vector3.zero, 3.0f, 1.0f, Accent(p));
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f * Mathf.Deg2Rad;
                Prims.Box("Seat" + i, lift, new Vector3(Mathf.Cos(ang) * 3.3f, -0.3f, Mathf.Sin(ang) * 3.3f), new Vector3(0.6f, 0.9f, 0.6f), Dark, Quaternion.Euler(0f, -i * 30f, 0f));
            }
            a.lift = lift; a.towerTop = 40f;
        }

        static void Swing(Poi p, Transform root, RideAnimator a)
        {
            Prims.Cyl("Base", root, new Vector3(0f, 0.15f, 0f), 9.5f, 0.3f, Lit("SwingDeck", new Color(0.85f, 0.85f, 0.88f)));
            Prims.Cyl("Tower", root, new Vector3(0f, 7f, 0f), 0.7f, 14f, Steel);
            var rotor = Prims.Node("Rotor", root, new Vector3(0f, 14f, 0f));
            Prims.Cyl("Rim", rotor, new Vector3(0f, 0f, 0f), 5f, 0.8f, Accent(p));
            Cone("Canopy", rotor, new Vector3(0f, 0.4f, 0f), 5.4f, 2.6f, White);
            Prims.Sphere("Star", rotor, new Vector3(0f, 3.3f, 0f), 0.6f, Gold);
            const int n = 16;
            var subs = new Transform[n]; var yaw = new float[n];
            for (int i = 0; i < n; i++)
            {
                float th = i * 360f / n;
                yaw[i] = -th;
                var piv = Prims.Node("Chain" + i, rotor, new Vector3(4.6f * Mathf.Cos(th * Mathf.Deg2Rad), -0.4f, 4.6f * Mathf.Sin(th * Mathf.Deg2Rad)), Quaternion.Euler(0f, -th, 0f));
                Prims.Box("Rope", piv, new Vector3(0f, -3f, 0f), new Vector3(0.05f, 6f, 0.05f), Dark, shadows: false);
                Prims.Box("Seat", piv, new Vector3(0f, -6.1f, 0f), new Vector3(0.55f, 0.12f, 0.55f), i % 2 == 0 ? Accent(p) : Gold);
                subs[i] = piv;
            }
            a.rotor = rotor; a.subs = subs; a.subBaseYaw = yaw;
        }

        static void LogFlume(Poi p, Transform root, RideAnimator a)
        {
            var heights = new[] { 0f, 0.6f, 0.5f, 0.6f, 0.72f, 9f, 0.78f, 9f, 0.84f, 0.6f, 1f, 0.6f };
            var pts = OvalTrack(110, p.size.x * 0.5f - 4f, p.size.y * 0.5f - 4f, 3f, heights);
            var b = new MeshBatch();
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                var s0 = pts[i]; var s1 = pts[(i + 1) % n];
                var tan = (s1 - s0).normalized; var side = Vector3.Cross(Vector3.up, tan).normalized;
                b.Box(Prims.Beam(s0 - Vector3.up * 0.15f, s1 - Vector3.up * 0.15f, 2.6f, 0.3f), 0);                  // trough
                b.Box(Prims.Beam(s0 + side * 1.35f + Vector3.up * 0.25f, s1 + side * 1.35f + Vector3.up * 0.25f, 0.2f, 0.9f), 0);
                b.Box(Prims.Beam(s0 - side * 1.35f + Vector3.up * 0.25f, s1 - side * 1.35f + Vector3.up * 0.25f, 0.2f, 0.9f), 0);
                b.Box(Prims.Beam(s0 + Vector3.up * 0.08f, s1 + Vector3.up * 0.08f, 2.3f, 0.05f), 1);                // water
                if (i % 3 == 0 && s0.y > 1.3f) b.Box(Matrix4x4.TRS(new Vector3(s0.x, (s0.y - 0.3f) * 0.5f, s0.z), Quaternion.identity, new Vector3(0.4f, s0.y - 0.3f, 0.4f)), 2);
            }
            b.Emit("Flume_" + p.id, root, new[] { Wood, Water, Lit("FlumeSupport", new Color(0.45f, 0.30f, 0.16f)) }, S);
            Prims.Box("SplashPool", root, new Vector3(pts[(int)(0.88f * n)].x, 0.05f, pts[(int)(0.88f * n)].z), new Vector3(9f, 0.1f, 7f), Water, shadows: false);
            Prims.Box("Rock", root, new Vector3(0f, 3f, 2f), new Vector3(14f, 6f, 6f), Lit("Rock", new Color(0.55f, 0.50f, 0.44f)), Quaternion.Euler(0f, 12f, 0f));
            Prims.Box("Station", root, new Vector3(0f, 0.5f, -(p.size.y * 0.5f - 4f) - 2.4f), new Vector3(10f, 1f, 2.5f), Wood);
            a.track = pts;
            var boats = new Transform[10];
            for (int k = 0; k < boats.Length; k++)
            {
                var bn = Prims.Node("Log" + k, root, Vector3.zero);
                Prims.Box("Hull", bn, new Vector3(0f, 0.45f, 0f), new Vector3(1.3f, 0.6f, 2.8f), Lit("LogBoat", new Color(0.62f, 0.40f, 0.20f)));
                Prims.Box("Seat", bn, new Vector3(0f, 0.8f, -0.3f), new Vector3(1.0f, 0.3f, 1.2f), Accent(p));
                boats[k] = bn;
            }
            a.movers = boats;
        }

        static void PirateShip(Poi p, Transform root, RideAnimator a)
        {
            var b = new MeshBatch();
            foreach (float z in new[] { -3.5f, 3.5f })
            {
                b.Box(Prims.Beam(new Vector3(-7.5f, 0f, z), new Vector3(0f, 12.6f, z), 0.6f, 0.6f), 0);
                b.Box(Prims.Beam(new Vector3(7.5f, 0f, z), new Vector3(0f, 12.6f, z), 0.6f, 0.6f), 0);
            }
            b.Box(new Vector3(0f, 12.4f, 0f), new Vector3(0.7f, 0.7f, 8f), 0);
            b.Emit("Frame_" + p.id, root, new[] { Steel }, S);
            var pend = Prims.Node("Pendulum", root, new Vector3(0f, 12.2f, 0f));
            foreach (float z in new[] { -2.4f, 2.4f })
            {
                Prims.Box("Arm", pend, new Vector3(-2f, -4.5f, z), new Vector3(0.35f, 9.4f, 0.35f), Steel, Quaternion.Euler(0f, 0f, -24f));
                Prims.Box("Arm", pend, new Vector3(2f, -4.5f, z), new Vector3(0.35f, 9.4f, 0.35f), Steel, Quaternion.Euler(0f, 0f, 24f));
            }
            var hullMat = Lit("ShipHull", new Color(0.45f, 0.27f, 0.13f));
            Prims.Box("Hull", pend, new Vector3(0f, -9.4f, 0f), new Vector3(15f, 1.6f, 4.6f), hullMat);
            Prims.Box("Keel", pend, new Vector3(0f, -10.5f, 0f), new Vector3(11f, 1.0f, 3.0f), hullMat);
            Prims.Box("Bow", pend, new Vector3(-7.6f, -8.6f, 0f), new Vector3(1.6f, 2.4f, 4.0f), hullMat, Quaternion.Euler(0f, 0f, -15f));
            Prims.Box("Stern", pend, new Vector3(7.6f, -8.6f, 0f), new Vector3(1.6f, 2.4f, 4.0f), hullMat, Quaternion.Euler(0f, 0f, 15f));
            Prims.Box("Stripe", pend, new Vector3(0f, -9.0f, 0f), new Vector3(15.1f, 0.3f, 4.7f), Accent(p));
            Prims.Cyl("Mast", pend, new Vector3(0f, -5.2f, 0f), 0.15f, 7f, Wood);
            Prims.Box("Sail", pend, new Vector3(0f, -4.6f, 0.1f), new Vector3(4.2f, 3.6f, 0.08f), White);
            Prims.Box("Flag", pend, new Vector3(0.6f, -1.6f, 0f), new Vector3(1.2f, 0.7f, 0.05f), Lit("Black", new Color(0.08f, 0.08f, 0.08f)));
            Prims.Box("Platform", root, new Vector3(0f, 0.5f, -5.5f), new Vector3(14f, 1f, 3f), Lit("Concrete", new Color(0.75f, 0.74f, 0.72f)));
            a.pendulum = pend;
        }

        static void DarkRide(Poi p, Transform root, RideAnimator a)
        {
            var wall = Lit("MansionWall", new Color(0.33f, 0.29f, 0.40f));
            var roof = Lit("MansionRoof", new Color(0.17f, 0.15f, 0.22f));
            Prims.Box("House", root, new Vector3(0f, 5f, 1f), new Vector3(18f, 10f, 18f), wall);
            Prims.Box("Upper", root, new Vector3(0f, 12.5f, 1f), new Vector3(13f, 5f, 12f), wall);
            Prims.Box("RoofA", root, new Vector3(0f, 16.2f, -1.7f), new Vector3(13.6f, 0.4f, 7.2f), roof, Quaternion.Euler(-35f, 0f, 0f));
            Prims.Box("RoofB", root, new Vector3(0f, 16.2f, 3.7f), new Vector3(13.6f, 0.4f, 7.2f), roof, Quaternion.Euler(35f, 0f, 0f));
            foreach (var c in new[] { new Vector2(-9.5f, -8f), new Vector2(9.5f, -8f), new Vector2(-9.5f, 10f), new Vector2(9.5f, 10f) })
            {
                Prims.Cyl("Tower", root, new Vector3(c.x, 8f, c.y), 2f, 16f, wall);
                Cone("TowerRoof", root, new Vector3(c.x, 16f, c.y), 2.6f, 5f, roof);
            }
            for (int r = 0; r < 2; r++)
                for (int i = -2; i <= 2; i++)
                    Prims.Box("Window", root, new Vector3(i * 3f, 3.5f + r * 4f, -8.05f), new Vector3(1.1f, 1.6f, 0.1f), Glow, shadows: false);
            Prims.Box("Door", root, new Vector3(0f, 1.7f, -8.1f), new Vector3(2.6f, 3.4f, 0.2f), Lit("Black", new Color(0.08f, 0.08f, 0.08f)));
            var vane = Prims.Node("Vane", root, new Vector3(0f, 18.4f, 1f));
            Prims.Box("Arrow", vane, Vector3.zero, new Vector3(0.1f, 0.3f, 2.2f), Dark);
            a.rotor = null;
        }

        static void FerrisWheel(Poi p, Transform root, RideAnimator a)
        {
            const float R = 13f, H = 16f;
            var b = new MeshBatch();
            foreach (float z in new[] { -2.6f, 2.6f })
            {
                b.Box(Prims.Beam(new Vector3(-9f, 0f, z), new Vector3(0f, H + 0.3f, z * 0.45f), 0.7f, 0.7f), 0);
                b.Box(Prims.Beam(new Vector3(9f, 0f, z), new Vector3(0f, H + 0.3f, z * 0.45f), 0.7f, 0.7f), 0);
            }
            b.Emit("Frame_" + p.id, root, new[] { White }, S);
            Prims.Box("Platform", root, new Vector3(0f, 1.2f, -2.8f), new Vector3(6f, 2.4f, 3f), Lit("Concrete", new Color(0.75f, 0.74f, 0.72f)));
            var rotor = Prims.Node("Wheel", root, new Vector3(0f, H, 0f));
            var wb = new MeshBatch();
            const int segs = 32;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                foreach (float z in new[] { -1.1f, 1.1f })
                    wb.Box(Prims.Beam(new Vector3(R * Mathf.Cos(a0), R * Mathf.Sin(a0), z), new Vector3(R * Mathf.Cos(a1), R * Mathf.Sin(a1), z), 0.3f, 0.3f), 0);
                if (i % 2 == 0)
                    foreach (float z in new[] { -1.1f, 1.1f })
                        wb.Box(Prims.Beam(new Vector3(0f, 0f, z * 0.4f), new Vector3(R * Mathf.Cos(a0), R * Mathf.Sin(a0), z), 0.12f, 0.12f), 1);
            }
            wb.Emit("Rim_" + p.id, rotor, new[] { Accent(p), White }, S);
            Prims.Cyl("Hub", rotor, Vector3.zero, 1.2f, 3.2f, Steel, Quaternion.Euler(90f, 0f, 0f));
            const int n = 16;
            var subs = new Transform[n];
            var cabin = new[] { Lit("Cabin1", Hex("#e84a5f")), Lit("Cabin2", Hex("#2a9df4")), Lit("Cabin3", Hex("#3dbb5c")), Lit("Cabin4", Hex("#f7b733")) };
            for (int i = 0; i < n; i++)
            {
                float ang = i * Mathf.PI * 2f / n;
                var g = Prims.Node("Gondola" + i, rotor, new Vector3(R * Mathf.Cos(ang), R * Mathf.Sin(ang), 0f));
                Prims.Box("Cabin", g, new Vector3(0f, -1.3f, 0f), new Vector3(1.6f, 1.7f, 1.6f), cabin[i % cabin.Length]);
                Prims.Box("Roof", g, new Vector3(0f, -0.35f, 0f), new Vector3(1.8f, 0.2f, 1.8f), White);
                subs[i] = g;
            }
            a.rotor = rotor; a.subs = subs;
        }

        static void Carousel(Poi p, Transform root, RideAnimator a)
        {
            Prims.Cyl("Base", root, new Vector3(0f, 0.25f, 0f), 8.6f, 0.5f, Lit("Concrete", new Color(0.75f, 0.74f, 0.72f)));
            var rotor = Prims.Node("Rotor", root, new Vector3(0f, 0.5f, 0f));
            Prims.Cyl("Platform", rotor, new Vector3(0f, 0.2f, 0f), 8f, 0.4f, Lit("CarouselFloor", Hex("#f5e1c8")));
            Prims.Cyl("Column", rotor, new Vector3(0f, 3f, 0f), 1.1f, 5.6f, Accent(p));
            Prims.Cyl("Rim", rotor, new Vector3(0f, 5.8f, 0f), 8.8f, 0.9f, White);
            Cone("Canopy", rotor, new Vector3(0f, 6.2f, 0f), 9.2f, 3.4f, Accent(p));
            Prims.Sphere("Top", rotor, new Vector3(0f, 9.8f, 0f), 0.5f, Gold);
            var horseMat = new[] { White, Lit("HorseBrown", Hex("#a0522d")), Lit("HorseGray", Hex("#8a8f99")) };
            var subs = new List<Transform>(); var baseY = new List<float>();
            for (int ring = 0; ring < 2; ring++)
            {
                int cnt = ring == 0 ? 12 : 8; float r = ring == 0 ? 6.4f : 4.0f;
                for (int i = 0; i < cnt; i++)
                {
                    float th = (i + ring * 0.5f) * Mathf.PI * 2f / cnt;
                    var pos = new Vector3(r * Mathf.Cos(th), 0f, r * Mathf.Sin(th));
                    Prims.Cyl("Pole", rotor, pos + Vector3.up * 3f, 0.06f, 5.4f, Gold);
                    var h = Prims.Node("Horse", rotor, pos + Vector3.up * 1.6f, Quaternion.Euler(0f, -th * Mathf.Rad2Deg, 0f));
                    var m = horseMat[(i + ring) % horseMat.Length];
                    Prims.Box("Body", h, Vector3.zero, new Vector3(0.35f, 0.55f, 1.3f), m);
                    Prims.Box("Head", h, new Vector3(0f, 0.45f, 0.65f), new Vector3(0.28f, 0.6f, 0.35f), m, Quaternion.Euler(-25f, 0f, 0f));
                    Prims.Box("Legs", h, new Vector3(0f, -0.45f, 0f), new Vector3(0.3f, 0.5f, 1.0f), m);
                    subs.Add(h); baseY.Add(1.6f);
                }
            }
            a.rotor = rotor; a.subs = subs.ToArray(); a.subBaseY = baseY.ToArray();
        }

        static void Teacups(Poi p, Transform root, RideAnimator a)
        {
            Prims.Cyl("Base", root, new Vector3(0f, 0.15f, 0f), 8.6f, 0.3f, Lit("Concrete", new Color(0.75f, 0.74f, 0.72f)));
            var rotor = Prims.Node("Turntable", root, new Vector3(0f, 0.3f, 0f));
            Prims.Cyl("Disc", rotor, new Vector3(0f, 0.15f, 0f), 8f, 0.3f, Lit("TeacupFloor", Hex("#fff3e6")));
            Prims.Sphere("Teapot", rotor, new Vector3(0f, 1.6f, 0f), 1.9f, Accent(p));
            Cone("Lid", rotor, new Vector3(0f, 3.2f, 0f), 1f, 1.2f, Gold);
            var colors = new[] { Lit("Cup1", Hex("#ff8fab")), Lit("Cup2", Hex("#8ecae6")), Lit("Cup3", Hex("#ffd166")), Lit("Cup4", Hex("#b5e48c")) };
            var subs = new Transform[6];
            for (int i = 0; i < 6; i++)
            {
                float th = i * 60f * Mathf.Deg2Rad;
                var c = Prims.Node("Cup" + i, rotor, new Vector3(5.2f * Mathf.Cos(th), 0.35f, 5.2f * Mathf.Sin(th)));
                Prims.Cyl("Cup", c, new Vector3(0f, 0.6f, 0f), 1.4f, 1.2f, colors[i % colors.Length]);
                Prims.Cyl("Rim", c, new Vector3(0f, 1.22f, 0f), 1.45f, 0.08f, White);
                Prims.Cyl("Wheel", c, new Vector3(0f, 1.0f, 0f), 0.35f, 0.5f, Gold);
                Prims.Box("Handle", c, new Vector3(1.55f, 0.7f, 0f), new Vector3(0.25f, 0.6f, 0.2f), colors[i % colors.Length]);
                subs[i] = c;
            }
            a.rotor = rotor; a.subs = subs;
        }

        static void BumperCars(Poi p, Transform root, RideAnimator a)
        {
            float hx = p.size.x * 0.5f - 0.5f, hz = p.size.y * 0.5f - 0.5f;
            Prims.Box("Floor", root, new Vector3(0f, 0.12f, 0f), new Vector3(hx * 2f, 0.24f, hz * 2f), Lit("RinkFloor", new Color(0.28f, 0.29f, 0.33f), 0.6f, 0.3f));
            var b = new MeshBatch();
            b.Box(new Vector3(0f, 0.45f, hz), new Vector3(hx * 2f, 0.6f, 0.3f), 0);
            b.Box(new Vector3(0f, 0.45f, -hz), new Vector3(hx * 2f, 0.6f, 0.3f), 0);
            b.Box(new Vector3(hx, 0.45f, 0f), new Vector3(0.3f, 0.6f, hz * 2f), 0);
            b.Box(new Vector3(-hx, 0.45f, 0f), new Vector3(0.3f, 0.6f, hz * 2f), 0);
            foreach (var sx in new[] { -1f, 0f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    b.Box(new Vector3(sx * hx, 2.6f, sz * hz), new Vector3(0.3f, 5.2f, 0.3f), 1);
            b.Box(new Vector3(0f, 5.2f, hz), new Vector3(hx * 2f + 0.4f, 0.5f, 0.5f), 2);
            b.Box(new Vector3(0f, 5.2f, -hz), new Vector3(hx * 2f + 0.4f, 0.5f, 0.5f), 2);
            for (int i = -2; i <= 2; i++) b.Box(new Vector3(i * hx * 0.5f, 5.3f, 0f), new Vector3(0.25f, 0.25f, hz * 2f), 1);
            b.Emit("Rink_" + p.id, root, new[] { Lit("RinkWall", Hex("#ffd166")), Steel, Accent(p) }, S);
            var colors = new[] { Lit("Bumper1", Hex("#e63946")), Lit("Bumper2", Hex("#457b9d")), Lit("Bumper3", Hex("#2a9d8f")), Lit("Bumper4", Hex("#f4a261")), Lit("Bumper5", Hex("#9b5de5")) };
            var cars = new Transform[10];
            for (int k = 0; k < cars.Length; k++)
            {
                var c = Prims.Node("BumperCar" + k, root, Vector3.zero);
                Prims.Box("Body", c, new Vector3(0f, 0.55f, 0f), new Vector3(1.1f, 0.5f, 1.7f), colors[k % colors.Length]);
                Prims.Cyl("Bumper", c, new Vector3(0f, 0.38f, 0f), 0.95f, 0.18f, Dark);
                Prims.Cyl("Pole", c, new Vector3(0f, 2.6f, -0.6f), 0.04f, 3.8f, Steel);
                cars[k] = c;
            }
            a.movers = cars; a.extent = new Vector2(hx - 1.5f, hz - 1.6f);
        }

        static void KiddieTrain(Poi p, Transform root, RideAnimator a)
        {
            var pts = OvalTrack(80, p.size.x * 0.5f - 2.2f, p.size.y * 0.5f - 2.2f, 0f, new[] { 0f, 0.15f, 1f, 0.15f });
            TrackMesh("KTrack_" + p.id, root, pts, Steel, Wood, 0.9f, 0, 0.12f);
            a.track = pts; a.spacing = 1.9f;
            Prims.Box("Tunnel", root, new Vector3(0f, 1.4f, p.size.y * 0.5f - 2.2f), new Vector3(3.2f, 2.8f, 4f), Lit("TunnelRock", new Color(0.55f, 0.50f, 0.44f)));
            Prims.Box("Station", root, new Vector3(0f, 0.3f, -(p.size.y * 0.5f - 2.2f) - 1.6f), new Vector3(5f, 0.6f, 1.6f), Wood);
            a.movers = Train(root, 4, new Vector3(1.0f, 0.6f, 1.6f), Accent(p), Lit("TrainRed", Hex("#e63946")), engine: true);
        }

        static void MiniCoaster(Poi p, Transform root, RideAnimator a)
        {
            var heights = new[] { 0f, 1f, 0.1f, 1f, 0.3f, 4.2f, 0.45f, 1.5f, 0.6f, 3.2f, 0.75f, 1.2f, 1f, 1f };
            var pts = OvalTrack(80, p.size.x * 0.5f - 2f, p.size.y * 0.5f - 2f, 1f, heights);
            TrackMesh("MTrack_" + p.id, root, pts, Accent(p), White, 0.9f, 3, 0.15f);
            a.track = pts; a.spacing = 1.7f;
            Prims.Box("Station", root, new Vector3(0f, 0.4f, -(p.size.y * 0.5f - 2f) - 1.5f), new Vector3(5f, 0.8f, 1.6f), Lit("Concrete", new Color(0.75f, 0.74f, 0.72f)));
            a.movers = Train(root, 3, new Vector3(1.0f, 0.6f, 1.5f), Lit("MiniCar", Hex("#2a9df4")), Gold);
        }

        // ------------------------------------------------------------------ food / rest

        static void Umbrella(Transform root, Vector3 pos, Material top)
        {
            Prims.Cyl("Table", root, pos + Vector3.up * 0.38f, 0.55f, 0.76f, White);
            Prims.Cyl("Pole", root, pos + Vector3.up * 1.4f, 0.04f, 2.0f, Steel);
            Cone("Umbrella", root, pos + Vector3.up * 2.2f, 1.3f, 0.6f, top);
        }

        static void FoodCourt(Poi p, Transform root)
        {
            Prims.Box("Floor", root, new Vector3(0f, 0.05f, 0f), new Vector3(p.size.x, 0.1f, p.size.y), Lit("Tiles", Hex("#efe6d8")), shadows: false);
            Prims.Box("Kitchen", root, new Vector3(0f, 2.2f, p.size.y * 0.5f - 2f), new Vector3(p.size.x, 4.4f, 4f), Lit("FoodWall", Hex("#f2efe9")));
            Prims.Box("Sign", root, new Vector3(0f, 4.0f, p.size.y * 0.5f - 4.05f), new Vector3(p.size.x * 0.8f, 0.9f, 0.1f), Accent(p), shadows: false);
            Prims.Box("Roof", root, new Vector3(0f, 4.6f, 0.5f), new Vector3(p.size.x, 0.3f, p.size.y - 5f), Lit("FoodRoof", Hex("#e76f51")));
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 0f })
                    Prims.Box("Post", root, new Vector3(sx * (p.size.x * 0.5f - 0.3f), 2.3f, sz * (p.size.y * 0.5f - 0.4f)), new Vector3(0.25f, 4.6f, 0.25f), White);
            var tops = new[] { Lit("Umb1", Hex("#e9c46a")), Lit("Umb2", Hex("#2a9d8f")) };
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 2; j++)
                    Prims.Cyl("Table", root, new Vector3(-6f + i * 4f, 0.38f, -4.5f + j * 3.5f), 0.6f, 0.76f, White);
            for (int i = 0; i < 3; i++) Umbrella(root, new Vector3(-5f + i * 5f, 0f, -p.size.y * 0.5f + 1.2f), tops[i % 2]);
        }

        static void SnackBar(Poi p, Transform root)
        {
            Prims.Box("Floor", root, new Vector3(0f, 0.05f, 0f), new Vector3(p.size.x, 0.1f, p.size.y), Lit("Tiles", Hex("#efe6d8")), shadows: false);
            float kx = Mathf.Min(p.size.x * 0.6f, 6f);
            Prims.Box("Kiosk", root, new Vector3(0f, 1.6f, p.size.y * 0.5f - 1.4f), new Vector3(kx, 3.2f, 2.4f), Lit("KioskWall", Hex("#fdf6ec")));
            Prims.Box("Awning", root, new Vector3(0f, 3.0f, p.size.y * 0.5f - 3.0f), new Vector3(kx + 0.6f, 0.15f, 1.6f), Accent(p), Quaternion.Euler(-18f, 0f, 0f));
            var tops = new[] { Lit("Umb1", Hex("#e9c46a")), Lit("Umb2", Hex("#2a9d8f")) };
            int n = p.size.x >= 12f ? 3 : 2;
            for (int i = 0; i < n; i++)
                Umbrella(root, new Vector3((i - (n - 1) * 0.5f) * Mathf.Min(4.2f, p.size.x / n), 0f, -p.size.y * 0.5f + 2f), tops[i % 2]);
        }

        static void RestArea(Poi p, Transform root)
        {
            Prims.Box("Floor", root, new Vector3(0f, 0.04f, 0f), new Vector3(p.size.x, 0.08f, p.size.y), Lit("PlazaStone", Hex("#d8d2c4")), shadows: false);
            var bench = Wood;
            float hx = p.size.x * 0.5f - 0.6f, hz = p.size.y * 0.5f - 0.6f;
            foreach (var s in new[] { -1f, 1f })
            {
                Prims.Box("Bench", root, new Vector3(s * hx, 0.45f, 0f), new Vector3(0.5f, 0.1f, Mathf.Min(3.5f, p.size.y - 2f)), bench);
                Prims.Box("BenchBack", root, new Vector3(s * (hx + 0.22f), 0.8f, 0f), new Vector3(0.08f, 0.6f, Mathf.Min(3.5f, p.size.y - 2f)), bench);
                Prims.Box("Bench", root, new Vector3(0f, 0.45f, s * hz), new Vector3(Mathf.Min(3.5f, p.size.x - 2f), 0.1f, 0.5f), bench);
            }
            var leaf = Lit("Hedge", Hex("#4c9a2a"));
            foreach (var c in new[] { new Vector2(-1f, -1f), new Vector2(1f, 1f) })
            {
                Prims.Box("Planter", root, new Vector3(c.x * (hx - 0.8f), 0.3f, c.y * (hz - 0.8f)), new Vector3(1.2f, 0.6f, 1.2f), Lit("PlanterStone", Hex("#b8b0a0")));
                Prims.Sphere("Shrub", root, new Vector3(c.x * (hx - 0.8f), 1.0f, c.y * (hz - 0.8f)), 0.75f, leaf);
            }
        }
    }
}
