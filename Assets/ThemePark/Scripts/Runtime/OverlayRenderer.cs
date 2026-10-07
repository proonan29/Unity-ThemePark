using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThemePark
{
    public enum HeatMode { Off, Live, Cumulative }
    public enum EdgeMode { Off, Density, Flow }

    /// <summary>Heat map (live density or cumulative presence) and walkway colouring (level of service or flow) drawn over the park.</summary>
    [ExecuteAlways]
    public class OverlayRenderer : MonoBehaviour
    {
        public SimRunner runner;
        public Shader overlayShader;
        public HeatMode heat = HeatMode.Off;
        public EdgeMode edges = EdgeMode.Off;
        [Range(0f, 1f)] public float heatOpacity = 0.85f;
        public float refreshSeconds = 0.25f;

        Material heatMat, edgeMat;
        Texture2D tex;
        Color32[] pixels;
        Mesh quad, edgeMesh;
        Color[] edgeColors;
        float nextRefresh;
        float lastSimT = -1f;
        HeatMode lastHeat; EdgeMode lastEdges;
        public float MaxCumulative { get; private set; }

        void OnEnable() { RenderPipelineManager.beginCameraRendering += OnBeginCamera; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; }

        // live density (persons/m²) colour ramp
        static readonly float[] RampX = { 0.0f, 0.04f, 0.15f, 0.35f, 0.7f, 1.2f, 2.2f };
        static readonly Color[] RampC =
        {
            new Color(0.10f, 0.30f, 1.00f, 0.00f), new Color(0.10f, 0.35f, 1.00f, 0.35f), new Color(0.10f, 0.80f, 0.90f, 0.55f),
            new Color(0.25f, 0.90f, 0.30f, 0.65f), new Color(1.00f, 0.90f, 0.15f, 0.75f), new Color(1.00f, 0.45f, 0.05f, 0.85f),
            new Color(0.85f, 0.05f, 0.10f, 0.92f)
        };

        public static Color Ramp(float x)
        {
            if (x <= RampX[0]) return RampC[0];
            for (int i = 1; i < RampX.Length; i++)
                if (x <= RampX[i]) return Color.Lerp(RampC[i - 1], RampC[i], (x - RampX[i - 1]) / (RampX[i] - RampX[i - 1]));
            return RampC[RampC.Length - 1];
        }

        static readonly float[] FlowX = { 0f, 100f, 400f, 1000f, 2000f, 3500f };
        public static Color FlowColor(float perHour)
        {
            var c = new[] { new Color(0.6f, 0.6f, 0.6f, 0.35f), new Color(0.2f, 0.6f, 1f, 0.8f), new Color(0.2f, 0.85f, 0.4f, 0.85f),
                            new Color(1f, 0.85f, 0.2f, 0.9f), new Color(1f, 0.45f, 0.1f, 0.95f), new Color(0.85f, 0.05f, 0.15f, 1f) };
            if (perHour <= FlowX[0]) return c[0];
            for (int i = 1; i < FlowX.Length; i++)
                if (perHour <= FlowX[i]) return Color.Lerp(c[i - 1], c[i], (perHour - FlowX[i - 1]) / (FlowX[i] - FlowX[i - 1]));
            return c[c.Length - 1];
        }

        Material NewMat(string name)
        {
            var sh = overlayShader != null ? overlayShader : Shader.Find("ThemePark/Overlay");
            if (sh == null) return null;
            return new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
        }

        void EnsureResources(ParkSim sim)
        {
            var tel = sim.tel;
            if (tex == null || tex.width != tel.gw || tex.height != tel.gh)
            {
                tex = new Texture2D(tel.gw, tel.gh, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                pixels = new Color32[tel.gw * tel.gh];
                lastSimT = -1f;
            }
            if (heatMat == null) heatMat = NewMat("HeatOverlay");
            if (edgeMat == null) edgeMat = NewMat("EdgeOverlay");
            if (heatMat != null) heatMat.SetTexture("_BaseMap", tex);
            if (quad == null)
            {
                var b = sim.L.bounds;
                float y = 0.12f;
                quad = new Mesh { name = "HeatQuad", hideFlags = HideFlags.DontSave };
                float x0 = tel.origin.x, z0 = tel.origin.y, x1 = x0 + tel.gw * Telemetry.Cell, z1 = z0 + tel.gh * Telemetry.Cell;
                quad.vertices = new[] { new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0) };
                quad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quad.RecalculateBounds();
            }
            if (edgeMesh == null) BuildEdgeMesh(sim.L);
        }

        void BuildEdgeMesh(ParkLayout L)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            foreach (var e in L.edges)
            {
                Vector2 a = L.nodes[e.a].pos, b = L.nodes[e.b].pos;
                var d = (b - a).normalized; var r = new Vector2(d.y, -d.x) * (Mathf.Max(1.6f, e.width * 0.55f) * 0.5f);
                a += d * 1.2f; b -= d * 1.2f;
                int k = v.Count;
                float y = 0.14f;
                v.Add(new Vector3(a.x - r.x, y, a.y - r.y)); v.Add(new Vector3(a.x + r.x, y, a.y + r.y));
                v.Add(new Vector3(b.x + r.x, y, b.y + r.y)); v.Add(new Vector3(b.x - r.x, y, b.y - r.y));
                uv.Add(Vector2.zero); uv.Add(Vector2.zero); uv.Add(Vector2.zero); uv.Add(Vector2.zero);
                t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            edgeMesh = new Mesh { name = "EdgeOverlay", hideFlags = HideFlags.DontSave };
            edgeMesh.SetVertices(v); edgeMesh.SetUVs(0, uv); edgeMesh.SetTriangles(t, 0);
            edgeColors = new Color[v.Count];
            edgeMesh.colors = edgeColors;
            edgeMesh.RecalculateBounds();
        }

        /// <summary>Recompute texture / colours from the telemetry (throttled).</summary>
        public void Refresh(bool force = false)
        {
            var sim = runner != null ? runner.Sim : null;
            if (sim == null) return;
            if (!force && Application.isPlaying && Time.unscaledTime < nextRefresh && heat == lastHeat && edges == lastEdges) return;
            nextRefresh = Time.unscaledTime + refreshSeconds;
            if (!force && Mathf.Approximately(lastSimT, sim.t) && heat == lastHeat && edges == lastEdges) return;
            lastSimT = sim.t; lastHeat = heat; lastEdges = edges;
            EnsureResources(sim);
            var tel = sim.tel;
            if (heat != HeatMode.Off)
            {
                float inv = 1f / (Telemetry.Cell * Telemetry.Cell);
                float max = 1e-3f;
                if (heat == HeatMode.Cumulative) { foreach (var c in tel.heatCum) if (c > max) max = c; MaxCumulative = max * inv / 3600f; }
                float logMax = Mathf.Log(1f + max);
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color c;
                    if (heat == HeatMode.Live) c = Ramp(tel.heatLive[i] * inv);
                    else
                    {
                        float f = tel.heatCum[i] <= 0f ? 0f : Mathf.Log(1f + tel.heatCum[i]) / logMax;
                        c = Ramp(f * 2.2f);
                    }
                    c.a *= heatOpacity;
                    pixels[i] = c;
                }
                tex.SetPixels32(pixels);
                tex.Apply(false);
            }
            if (edges != EdgeMode.Off && edgeMesh != null)
            {
                for (int e = 0; e < sim.L.edges.Count; e++)
                {
                    Color c = edges == EdgeMode.Density ? Walk.LosColors[Walk.Los(sim.edgeDensity[e])] : FlowColor(tel.edgeRate[e]);
                    if (edges == EdgeMode.Density) c.a = 0.9f;
                    for (int k = 0; k < 4; k++) edgeColors[e * 4 + k] = c;
                }
                edgeMesh.colors = edgeColors;
            }
        }

        void Update() { if (Application.isPlaying) Refresh(); }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            var sim = runner != null ? runner.Sim : null;
            if (sim == null || (heat == HeatMode.Off && edges == EdgeMode.Off)) return;
            if (!Application.isPlaying) Refresh();
            EnsureResources(sim);
            if (heat != HeatMode.Off && heatMat != null)
                Graphics.RenderMesh(new RenderParams(heatMat) { camera = cam, layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }, quad, 0, Matrix4x4.identity);
            if (edges != EdgeMode.Off && edgeMat != null)
                Graphics.RenderMesh(new RenderParams(edgeMat) { camera = cam, layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }, edgeMesh, 0, Matrix4x4.identity);
        }

        public void ResetResources()
        {
            quad = null; edgeMesh = null; tex = null; lastSimT = -1f;
        }
    }
}
