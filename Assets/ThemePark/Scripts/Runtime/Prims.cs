using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThemePark
{
    public interface IMaterialProvider
    {
        Material Get(string key, Color color, float smoothness = 0.3f, float metallic = 0f);
        Material Unlit(string key, Color color);
        Material Shader(string key, string shader, Color color);
    }

    /// <summary>Where generated meshes go: the editor saves them as assets, play mode keeps them in memory.</summary>
    public interface IMeshStore
    {
        Mesh Save(string key, Mesh mesh);
    }

    public class RuntimeMeshStore : IMeshStore
    {
        public Mesh Save(string key, Mesh mesh) { mesh.name = key; return mesh; }
    }

    /// <summary>In-memory materials (play mode / previews).</summary>
    public class RuntimeMaterialProvider : IMaterialProvider
    {
        readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        public Material Get(string key, Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = Prims.NewLit(key, color, smoothness, metallic);
            cache[key] = m;
            return m;
        }
        public Material Unlit(string key, Color color) => Shader(key, "Universal Render Pipeline/Unlit", color);
        public Material Shader(string key, string shader, Color color)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var sh = UnityEngine.Shader.Find(shader);
            m = new Material(sh != null ? sh : Prims.LitShader) { name = key };
            Prims.SetColor(m, color);
            cache[key] = m;
            return m;
        }
    }

    /// <summary>Primitive helpers (no colliders – nothing here uses physics).</summary>
    public static class Prims
    {
        public static Shader LitShader
        {
            get
            {
                var s = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
                return s != null ? s : UnityEngine.Shader.Find("Standard");
            }
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static Material NewLit(string name, Color color, float smoothness, float metallic)
        {
            var m = new Material(LitShader) { name = name, enableInstancing = true };
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            return m;
        }

        static readonly Dictionary<PrimitiveType, Mesh> primMeshes = new Dictionary<PrimitiveType, Mesh>();

        public static Mesh PrimMesh(PrimitiveType t)
        {
            if (primMeshes.TryGetValue(t, out var m) && m != null) return m;
            var g = GameObject.CreatePrimitive(t);
            m = g.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(g);
            primMeshes[t] = m;
            return m;
        }

        static void Strip(GameObject g)
        {
            var c = g.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.DestroyImmediate(c);
        }

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material m,
            Quaternion? rot = null, bool shadows = true)
        {
            var g = GameObject.CreatePrimitive(type);
            Strip(g);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localRotation = rot ?? Quaternion.identity;
            g.transform.localScale = scale;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material m, Quaternion? rot = null, bool shadows = true)
            => Prim(PrimitiveType.Cube, name, parent, localPos, size, m, rot, shadows);

        /// <summary>Cylinder whose axis is the local Y axis of <paramref name="rot"/>.</summary>
        public static GameObject Cyl(string name, Transform parent, Vector3 localPos, float radius, float height, Material m, Quaternion? rot = null)
            => Prim(PrimitiveType.Cylinder, name, parent, localPos, new Vector3(radius * 2f, height * 0.5f, radius * 2f), m, rot);

        public static GameObject Sphere(string name, Transform parent, Vector3 localPos, float radius, Material m)
            => Prim(PrimitiveType.Sphere, name, parent, localPos, Vector3.one * radius * 2f, m);

        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material m, bool shadows = true)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        public static Transform Node(string name, Transform parent, Vector3 localPos, Quaternion? rot = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = rot ?? Quaternion.identity;
            return t;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(t.GetChild(i).gameObject);
        }

        /// <summary>Box from a to b (thin beam) with the given cross-section.</summary>
        public static Matrix4x4 Beam(Vector3 a, Vector3 b, float w, float h)
        {
            var d = b - a;
            float len = d.magnitude;
            var rot = len > 1e-5f ? Quaternion.LookRotation(d / len, Mathf.Abs(d.y / Mathf.Max(len, 1e-5f)) > 0.99f ? Vector3.forward : Vector3.up) : Quaternion.identity;
            return Matrix4x4.TRS((a + b) * 0.5f, rot, new Vector3(w, h, len));
        }
    }

    /// <summary>Low-poly procedural meshes (kept small so generated assets stay light).</summary>
    public static class ProcMesh
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        static Mesh Cached(string key, System.Func<Mesh> make)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = make(); m.name = key;
            cache[key] = m;
            return m;
        }

        /// <summary>Unit cylinder: radius 0.5, y from 0 to 1 (pivot at the base).</summary>
        public static Mesh Cylinder(int seg) => Cached("cyl" + seg, () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * 0.5f); n.Add(d); v.Add(d * 0.5f + Vector3.up); n.Add(d);
            }
            for (int i = 0; i < seg; i++) { int k = i * 2; tri.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 }); }
            Cap(v, n, tri, seg, 1f, Vector3.up, 0.5f);
            Cap(v, n, tri, seg, 0f, Vector3.down, 0.5f);
            return Build(v, n, tri);
        });

        /// <summary>Unit cone: base radius 0.5 at y 0, apex at y 1.</summary>
        public static Mesh Cone(int seg) => Cached("cone" + seg, () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg, am = (a0 + a1) * 0.5f;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.5f; var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.5f;
                var nm = (new Vector3(Mathf.Cos(am), 0.5f, Mathf.Sin(am))).normalized;
                int k = v.Count;
                v.Add(p0); v.Add(Vector3.up); v.Add(p1); n.Add(nm); n.Add(nm); n.Add(nm);
                tri.AddRange(new[] { k, k + 1, k + 2 });
            }
            Cap(v, n, tri, seg, 0f, Vector3.down, 0.5f);
            return Build(v, n, tri);
        });

        /// <summary>Unit sphere (radius 0.5) with flat-ish low poly look.</summary>
        public static Mesh Sphere(int lon, int lat) => Cached($"sph{lon}x{lat}", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int j = 0; j <= lat; j++)
            {
                float th = Mathf.PI * j / lat;
                for (int i = 0; i <= lon; i++)
                {
                    float ph = 2f * Mathf.PI * i / lon;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(d * 0.5f); n.Add(d);
                }
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + lon + 1;
                    tri.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            return Build(v, n, tri);
        });

        static void Cap(List<Vector3> v, List<Vector3> n, List<int> tri, int seg, float y, Vector3 normal, float r)
        {
            int c = v.Count;
            v.Add(new Vector3(0f, y, 0f)); n.Add(normal);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); n.Add(normal);
            }
            for (int i = 0; i < seg; i++)
            {
                if (normal.y > 0f) tri.AddRange(new[] { c, c + i + 2, c + i + 1 });
                else tri.AddRange(new[] { c, c + i + 1, c + i + 2 });
            }
        }

        static Mesh Build(List<Vector3> v, List<Vector3> n, List<int> tri)
        {
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The "person" used for visitors: body + head, 1.7 m tall, pivot at the feet, facing +Z.</summary>
        public static Mesh Person() => Cached("person", () =>
        {
            var b = new MeshBatch();
            var body = Cylinder(8); var sph = Sphere(8, 6); var cone = Cone(8);
            b.Add(cone, Matrix4x4.TRS(new Vector3(0, 0.0f, 0), Quaternion.identity, new Vector3(0.50f, 1.05f, 0.40f)), 0);   // legs/coat
            b.Add(body, Matrix4x4.TRS(new Vector3(0, 0.80f, 0), Quaternion.identity, new Vector3(0.40f, 0.55f, 0.30f)), 0); // torso
            b.Add(sph, Matrix4x4.TRS(new Vector3(0, 1.52f, 0), Quaternion.identity, new Vector3(0.26f, 0.28f, 0.26f)), 0);  // head
            b.Add(sph, Matrix4x4.TRS(new Vector3(0, 1.22f, 0.13f), Quaternion.identity, new Vector3(0.18f, 0.12f, 0.10f)), 0); // front marker
            return b.Combine(1)[0];
        });
    }

    /// <summary>Collects (mesh, matrix) pieces per material slot and combines them into one mesh per slot.</summary>
    public class MeshBatch
    {
        readonly List<List<CombineInstance>> slots = new List<List<CombineInstance>>();

        public void Add(Mesh mesh, Matrix4x4 m, int slot)
        {
            while (slots.Count <= slot) slots.Add(new List<CombineInstance>());
            slots[slot].Add(new CombineInstance { mesh = mesh, transform = m, subMeshIndex = 0 });
        }

        public void Box(Matrix4x4 m, int slot) => Add(Prims.PrimMesh(PrimitiveType.Cube), m, slot);
        public void Box(Vector3 c, Vector3 size, int slot, Quaternion? rot = null) => Box(Matrix4x4.TRS(c, rot ?? Quaternion.identity, size), slot);
        public bool Empty(int slot) => slot >= slots.Count || slots[slot].Count == 0;

        public Mesh[] Combine(int count)
        {
            var res = new Mesh[count];
            for (int s = 0; s < count; s++)
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32 };
                if (s < slots.Count && slots[s].Count > 0) m.CombineMeshes(slots[s].ToArray(), true, true);
                m.RecalculateBounds();
                res[s] = m;
            }
            return res;
        }

        /// <summary>Create one child object per non-empty slot.</summary>
        public void Emit(string name, Transform parent, Material[] mats, IMeshStore store, bool shadows = true)
        {
            var meshes = Combine(mats.Length);
            for (int s = 0; s < mats.Length; s++)
            {
                if (Empty(s)) continue;
                var mesh = store.Save(name + "_" + s, meshes[s]);
                Prims.MeshObject(name + (mats.Length > 1 ? "_" + mats[s].name : ""), parent, mesh, mats[s], shadows);
            }
        }
    }
}
