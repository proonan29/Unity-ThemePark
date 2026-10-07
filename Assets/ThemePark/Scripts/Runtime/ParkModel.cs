using System.Collections.Generic;
using UnityEngine;

namespace ThemePark
{
    public enum PoiKind { Gate, Ride, Food, Rest }

    public enum RideModel
    {
        Coaster, DropTower, Swing, LogFlume, PirateShip, DarkRide, FerrisWheel, Carousel, Teacups,
        BumperCars, KiddieTrain, MiniCoaster, FoodCourt, SnackBar, RestArea, Gate
    }

    public class PathNode
    {
        public int id;
        public string name;
        public Vector2 pos;      // world XZ
        public int zone;
        public readonly List<int> edges = new List<int>();
    }

    public class PathEdge
    {
        public int id, a, b;
        public float width, length;
        public int zone;
        public float Area => width * length;
        public int Other(int n) => n == a ? b : a;
    }

    public class Zone
    {
        public int id;
        public string name;
        public Color color;      // pavement tint
        public float area;       // walkable area (paths + queue/plaza areas), m²
    }

    /// <summary>Point of interest: ride, food, rest area or the gate. The entrance is a path-graph node;
    /// the queue area lies between the entrance and the building along <see cref="dir"/>.</summary>
    public class Poi
    {
        public int id;
        public string name;
        public PoiKind kind;
        public RideModel model;
        public int zone;
        public int node;                 // entrance node
        public Vector2 entrance;         // = node position
        public Vector2 dir;              // unit, entrance → building
        public float queueDepth;         // m, queue area depth (width = min(size.x, 10))
        public Vector2 size;             // x = across, y = along dir
        public Vector2 pos;              // footprint centre
        public float Yaw => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        public Color color;

        // rides: a batch of `batch` riders leaves every `interval` s and rides for `duration` s
        public int batch;
        public float interval, duration;
        public float popularity, thrill;
        public bool kidsRide;            // designed for small children
        public bool tallOnly;            // height restriction (families with small kids skip it)

        // food / rest
        public int capacity;
        public float serviceMin, serviceMax;

        // queue overflow: polyline from the entrance back along the walkways towards the gate
        public Vector2[] overflow;
        public int[] overflowEdges;
        public float[] overflowHalfW;
        public int queueCap;

        public float Throughput => interval > 0f ? batch * 3600f / interval : 0f;
        public float QueueWidth => Mathf.Min(size.x, 10f);
        public float QueueArea => queueDepth * QueueWidth;
        public bool IsRide => kind == PoiKind.Ride;

        /// <summary>World XZ of a point given in the POI frame (x across, y along dir, origin at the footprint centre).</summary>
        public Vector2 Local(float x, float y)
        {
            var right = new Vector2(dir.y, -dir.x);
            return pos + right * x + dir * y;
        }

        public bool Contains(Vector2 p, float margin = 0f)
        {
            var d = p - pos;
            var right = new Vector2(dir.y, -dir.x);
            return Mathf.Abs(Vector2.Dot(d, right)) <= size.x * 0.5f + margin && Mathf.Abs(Vector2.Dot(d, dir)) <= size.y * 0.5f + margin;
        }
    }

    public class ParkLayout
    {
        public string name;
        public readonly List<PathNode> nodes = new List<PathNode>();
        public readonly List<PathEdge> edges = new List<PathEdge>();
        public readonly List<Zone> zones = new List<Zone>();
        public readonly List<Poi> pois = new List<Poi>();
        public int gateNode;
        public Rect bounds;              // XZ (x = world x, y = world z)
        public float fenceZ;             // the fence line the gate sits in
        public float openTime = 9f * 3600f, closeTime = 21f * 3600f;

        public Poi Gate => pois.Find(p => p.kind == PoiKind.Gate);
        public IEnumerable<Poi> Rides { get { foreach (var p in pois) if (p.kind == PoiKind.Ride) yield return p; } }
        public Poi Find(string name) => pois.Find(p => string.Equals(p.name, name, System.StringComparison.OrdinalIgnoreCase));
        public PathNode Node(string name) => nodes.Find(n => n.name == name);

        public float TotalPathLength { get { float s = 0; foreach (var e in edges) s += e.length; return s; } }

        public Vector2 NodePos(int i) => nodes[i].pos;
    }

    /// <summary>Small helper to build a layout by names.</summary>
    public class LayoutBuilder
    {
        public readonly ParkLayout L = new ParkLayout();
        readonly Dictionary<string, int> byName = new Dictionary<string, int>();

        public int Zone(string name, Color c)
        {
            var z = new Zone { id = L.zones.Count, name = name, color = c };
            L.zones.Add(z);
            return z.id;
        }

        public int Node(string name, float x, float z, int zone)
        {
            var n = new PathNode { id = L.nodes.Count, name = name, pos = new Vector2(x, z), zone = zone };
            L.nodes.Add(n);
            byName[name] = n.id;
            return n.id;
        }

        public int Id(string name)
        {
            if (!byName.TryGetValue(name, out var i)) throw new System.ArgumentException("unknown node " + name);
            return i;
        }

        public PathEdge Edge(string a, string b, float width, int zone = -1)
        {
            int ia = Id(a), ib = Id(b);
            var na = L.nodes[ia]; var nb = L.nodes[ib];
            if (zone < 0) zone = na.zone == 0 ? nb.zone : na.zone;
            var e = new PathEdge { id = L.edges.Count, a = ia, b = ib, width = width, length = Vector2.Distance(na.pos, nb.pos), zone = zone };
            L.edges.Add(e);
            na.edges.Add(e.id); nb.edges.Add(e.id);
            return e;
        }

        /// <summary>Add a POI reached by a spur path from node <paramref name="from"/> to its entrance.</summary>
        public Poi Poi(string name, PoiKind kind, RideModel model, string from, Vector2 entrance, Vector2 dir,
            float queueDepth, Vector2 size, Color color, float spurWidth = 4f)
        {
            int zone = L.nodes[Id(from)].zone;
            string nodeName = "@" + name;
            Node(nodeName, entrance.x, entrance.y, zone);
            Edge(from, nodeName, spurWidth, zone);
            dir.Normalize();
            var p = new Poi
            {
                id = L.pois.Count, name = name, kind = kind, model = model, zone = zone, node = Id(nodeName),
                entrance = entrance, dir = dir, queueDepth = queueDepth, size = size, color = color,
                pos = entrance + dir * (queueDepth + size.y * 0.5f)
            };
            L.pois.Add(p);
            return p;
        }

        public ParkLayout Finish()
        {
            foreach (var z in L.zones) z.area = 0f;
            foreach (var e in L.edges) L.zones[e.zone].area += e.Area;
            foreach (var p in L.pois)
            {
                if (p.kind == PoiKind.Ride || p.kind == PoiKind.Food) L.zones[p.zone].area += p.QueueArea;
                if (p.kind == PoiKind.Rest || p.kind == PoiKind.Food) L.zones[p.zone].area += p.size.x * p.size.y;
            }
            // overflow polylines (first 250 m of the shortest way back to the gate)
            var G = new PathGraph(L);
            var nodes = new List<int>(); var edges = new List<int>();
            foreach (var p in L.pois)
            {
                p.queueCap = QueueLayout.Capacity(p);
                if (p.kind != PoiKind.Ride && p.kind != PoiKind.Food) continue;
                G.Run(p.node, e => e.length);
                if (!G.PathTo(L.gateNode, nodes, edges) || edges.Count == 0) continue;
                var pts = new List<Vector2>(); var es = new List<int>(); var hw = new List<float>();
                float len = 0f;
                pts.Add(L.nodes[nodes[0]].pos);
                for (int i = 0; i < edges.Count && len < 250f; i++)
                {
                    pts.Add(L.nodes[nodes[i + 1]].pos);
                    es.Add(edges[i]); hw.Add(L.edges[edges[i]].width * 0.5f);
                    len += L.edges[edges[i]].length;
                }
                p.overflow = pts.ToArray(); p.overflowEdges = es.ToArray(); p.overflowHalfW = hw.ToArray();
            }
            return L;
        }
    }

    public static class ParkPresets
    {
        public const string DefaultName = "Sunny Hills Park";
        public static readonly string[] Names = { DefaultName };

        public static ParkLayout Get(string name) => Default();

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        static Poi Ride(Poi p, int batch, float interval, float duration, float pop, float thrill, bool kids = false, bool tall = false)
        {
            p.batch = batch; p.interval = interval; p.duration = duration; p.popularity = pop; p.thrill = thrill;
            p.kidsRide = kids; p.tallOnly = tall;
            return p;
        }

        static Poi Service(Poi p, int capacity, float minMin, float maxMin)
        {
            p.capacity = capacity; p.serviceMin = minMin * 60f; p.serviceMax = maxMin * 60f;
            return p;
        }

        /// <summary>Sunny Hills Park: gate + main street in the south, a central hub and five themed lands.</summary>
        public static ParkLayout Default()
        {
            var b = new LayoutBuilder();
            b.L.name = DefaultName;
            b.L.bounds = new Rect(-142f, -134f, 284f, 276f);
            b.L.fenceZ = -116f;

            int zMain = b.Zone("Main Street", C("#d9cdb8"));
            int zAdv = b.Zone("Adventure Land", C("#c9a77c"));
            int zThr = b.Zone("Thrill Zone", C("#b9bcc4"));
            int zFan = b.Zone("Fantasy Land", C("#e6c3d4"));
            int zKid = b.Zone("Kids Land", C("#e8de9a"));
            int zAre = b.Zone("Arena & Food", C("#c7d9c4"));

            // main street and hub
            b.Node("GATE", 0, -126, zMain);
            b.Node("M0", 0, -106, zMain);
            b.L.gateNode = b.Id("GATE");
            b.Node("M1", 0, -78, zMain);
            b.Node("M2", 0, -58, zMain);
            b.Node("M3", 0, -40, zMain);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                b.Node("H" + i, 22f * Mathf.Cos(a), 22f * Mathf.Sin(a), zMain);
            }
            // lands (loops)
            b.Node("W0", -48, 0, zAdv); b.Node("W1", -68, 28, zAdv); b.Node("W2", -100, 32, zAdv);
            b.Node("W3", -118, 2, zAdv); b.Node("W4", -100, -30, zAdv); b.Node("W5", -68, -28, zAdv);
            b.Node("N0", 0, 48, zThr); b.Node("N1", 32, 66, zThr); b.Node("N2", 30, 100, zThr);
            b.Node("N3", 0, 116, zThr); b.Node("N4", -30, 100, zThr); b.Node("N5", -32, 66, zThr);
            b.Node("E0", 48, 0, zFan); b.Node("E1", 68, 28, zFan); b.Node("E2", 100, 32, zFan);
            b.Node("E3", 118, 2, zFan); b.Node("E4", 100, -30, zFan); b.Node("E5", 68, -28, zFan);
            b.Node("K0", -40, -48, zKid); b.Node("K1", -72, -58, zKid); b.Node("K2", -75, -88, zKid); b.Node("K3", -40, -88, zKid);
            b.Node("F0", 40, -48, zAre); b.Node("F1", 72, -58, zAre); b.Node("F2", 75, -88, zAre);
            b.Node("F23", 58, -88, zAre); b.Node("F3", 40, -88, zAre);

            // walkways (widths in m)
            b.Edge("GATE", "M0", 12f); b.Edge("M0", "M1", 10f); b.Edge("M1", "M2", 10f); b.Edge("M2", "M3", 10f); b.Edge("M3", "H6", 10f);
            for (int i = 0; i < 8; i++) b.Edge("H" + i, "H" + ((i + 1) % 8), 8f);
            b.Edge("H4", "W0", 6f); b.Edge("H2", "N0", 6f); b.Edge("H0", "E0", 6f); b.Edge("H5", "K0", 5f); b.Edge("H7", "F0", 5f);
            foreach (var land in new[] { "W", "N", "E" })
                for (int i = 0; i < 6; i++) b.Edge(land + i, land + ((i + 1) % 6), 5f);
            b.Edge("K0", "K1", 4.5f); b.Edge("K1", "K2", 4.5f); b.Edge("K2", "K3", 4.5f); b.Edge("K3", "K0", 4.5f);
            b.Edge("F0", "F1", 4.5f); b.Edge("F1", "F2", 4.5f); b.Edge("F2", "F23", 4.5f); b.Edge("F23", "F3", 4.5f); b.Edge("F3", "F0", 4.5f);
            // connectors (narrow back paths)
            b.Edge("W1", "N5", 3.5f); b.Edge("N1", "E1", 3.5f); b.Edge("W5", "K1", 3.5f); b.Edge("E5", "F1", 3.5f);
            b.Edge("M1", "K3", 3.5f); b.Edge("M1", "F3", 3.5f);

            var px = new Vector2(1, 0); var nx = new Vector2(-1, 0); var pz = new Vector2(0, 1); var nz = new Vector2(0, -1);

            // gate (no spur: the gate node itself)
            b.L.pois.Add(new Poi
            {
                id = b.L.pois.Count, name = "Main Gate", kind = PoiKind.Gate, model = RideModel.Gate, zone = zMain,
                node = b.Id("GATE"), entrance = new Vector2(0, -126), dir = pz, size = new Vector2(30, 6), pos = new Vector2(0, -116),
                color = C("#d24b3c")
            });

            // Thrill Zone
            Ride(b.Poi("Thunder Coaster", PoiKind.Ride, RideModel.Coaster, "N2", new Vector2(44, 100), px, 12f, new Vector2(36, 50), C("#e0402e")), 24, 120, 100, 1.0f, 1.0f, tall: true);
            Ride(b.Poi("Sky Drop", PoiKind.Ride, RideModel.DropTower, "N4", new Vector2(-44, 100), nx, 12f, new Vector2(14, 14), C("#7b3fd1")), 16, 105, 75, 0.85f, 0.95f, tall: true);
            Ride(b.Poi("Star Swing", PoiKind.Ride, RideModel.Swing, "N0", new Vector2(0, 60), pz, 10f, new Vector2(20, 20), C("#2e8fe0")), 32, 165, 130, 0.65f, 0.55f);
            Service(b.Poi("Thrill Snacks", PoiKind.Food, RideModel.SnackBar, "N3", new Vector2(0, 124), pz, 4f, new Vector2(16, 8), C("#f2a33a")), 160, 8, 15);
            Service(b.Poi("Thrill Garden", PoiKind.Rest, RideModel.RestArea, "N1", new Vector2(24, 76), new Vector2(-0.6f, 0.8f), 1f, new Vector2(9, 9), C("#6aa84f")), 0, 8, 18);

            // Adventure Land
            Ride(b.Poi("Log Flume", PoiKind.Ride, RideModel.LogFlume, "W2", new Vector2(-100, 46), pz, 12f, new Vector2(50, 30), C("#2fa6a0")), 8, 36, 330, 0.9f, 0.6f);
            Ride(b.Poi("Pirate Ship", PoiKind.Ride, RideModel.PirateShip, "W4", new Vector2(-100, -44), nz, 12f, new Vector2(24, 16), C("#8a5a2b")), 40, 210, 160, 0.7f, 0.65f);
            Ride(b.Poi("Haunted Mansion", PoiKind.Ride, RideModel.DarkRide, "W0", new Vector2(-60, 0), nx, 10f, new Vector2(24, 26), C("#4b3f63")), 4, 16, 300, 0.75f, 0.35f);
            Service(b.Poi("Adventure Camp", PoiKind.Rest, RideModel.RestArea, "W3", new Vector2(-128, 2), nx, 1f, new Vector2(12, 8), C("#6aa84f")), 0, 8, 18);

            // Fantasy Land
            Ride(b.Poi("Grand Wheel", PoiKind.Ride, RideModel.FerrisWheel, "E2", new Vector2(100, 46), pz, 10f, new Vector2(30, 16), C("#e8b923")), 6, 40, 720, 0.7f, 0.1f);
            Ride(b.Poi("Carousel", PoiKind.Ride, RideModel.Carousel, "E0", new Vector2(60, 0), px, 10f, new Vector2(20, 20), C("#e86fa3")), 48, 240, 180, 0.55f, 0.05f);
            Ride(b.Poi("Teacups", PoiKind.Ride, RideModel.Teacups, "E4", new Vector2(100, -44), nz, 10f, new Vector2(18, 18), C("#5fc9e8")), 36, 180, 120, 0.5f, 0.25f);
            Service(b.Poi("Fantasy Cafe", PoiKind.Food, RideModel.SnackBar, "E5", new Vector2(80, -20), px, 3f, new Vector2(8, 12), C("#f2a33a")), 200, 12, 25);

            // Kids Land
            Ride(b.Poi("Mini Coaster", PoiKind.Ride, RideModel.MiniCoaster, "K3", new Vector2(-48, -78), new Vector2(-0.6f, 0.8f), 6f, new Vector2(16, 14), C("#ff8a3d")), 16, 120, 75, 0.55f, 0.4f, kids: true);
            Ride(b.Poi("Kiddie Train", PoiKind.Ride, RideModel.KiddieTrain, "K2", new Vector2(-86, -96), nx, 8f, new Vector2(14, 22), C("#3dbb5c")), 30, 300, 240, 0.4f, 0.0f, kids: true);

            // Arena & Food
            Ride(b.Poi("Bumper Cars", PoiKind.Ride, RideModel.BumperCars, "F23", new Vector2(58, -96), nz, 6f, new Vector2(22, 12), C("#d93a6a")), 24, 240, 180, 0.65f, 0.3f);
            Service(b.Poi("Food Court", PoiKind.Food, RideModel.FoodCourt, "F0", new Vector2(50, -61), new Vector2(0.6f, -0.8f), 4f, new Vector2(16, 14), C("#f2a33a")), 600, 20, 35);

            // Central plaza (rest) in the hub
            Service(b.Poi("Central Plaza", PoiKind.Rest, RideModel.RestArea, "H6", new Vector2(0, -12), pz, 0f, new Vector2(14, 14), C("#6aa84f")), 0, 8, 18);

            return b.Finish();
        }
    }
}
