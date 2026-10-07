using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThemePark
{
    /// <summary>Draws guide routes as glowing ribbons with moving chevrons, plus start pin and destination beacon.</summary>
    [ExecuteAlways]
    public class RouteView : MonoBehaviour
    {
        public Shader overlayShader;
        public Color primaryColor = new Color(0.10f, 0.90f, 1.00f, 0.95f);
        public Color secondaryColor = new Color(1.00f, 1.00f, 1.00f, 0.75f);
        public Color startColor = new Color(0.20f, 0.95f, 0.35f);
        public float width = 3.2f;

        GuideResult primary, secondary;
        Vector2? start;
        Vector3? beacon;
        Color beaconColor = Color.yellow;
        Mesh primaryMesh, secondaryMesh, cyl, sph;
        Material primaryMat, secondaryMat, pinMat, beaconMat, ringMat;
        Texture2D arrows;

        void OnEnable() { RenderPipelineManager.beginCameraRendering += OnBeginCamera; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; }

        public void Set(GuideResult main, GuideResult alt, Vector2? startPoint, Poi dest)
        {
            primary = main != null && main.ok ? main : null;
            secondary = alt != null && alt.ok ? alt : null;
            start = startPoint;
            beacon = dest != null ? new Vector3(dest.pos.x, 0f, dest.pos.y) : (Vector3?)null;
            if (dest != null) beaconColor = dest.color;
            primaryMesh = primary != null ? Ribbon(primary.points, width, 0.3f, primaryMesh) : null;
            secondaryMesh = secondary != null ? Ribbon(secondary.points, width * 0.6f, 0.2f, secondaryMesh) : null;
        }

        public void Clear() => Set(null, null, null, null);

        static Mesh Ribbon(List<Vector2> pts, float w, float y, Mesh m)
        {
            if (m == null) m = new Mesh { name = "Route", hideFlags = HideFlags.DontSave };
            m.Clear();
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var col = new List<Color>();
            float u = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 dPrev = i > 0 ? (pts[i] - pts[i - 1]).normalized : Vector2.zero;
                Vector2 dNext = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).normalized : Vector2.zero;
                var d = (dPrev + dNext); if (d.sqrMagnitude < 1e-6f) d = dNext.sqrMagnitude > 0 ? dNext : dPrev; d.Normalize();
                var r = new Vector2(d.y, -d.x);
                float miter = 1f;
                if (dPrev.sqrMagnitude > 0f && dNext.sqrMagnitude > 0f) miter = 1f / Mathf.Max(0.35f, Vector2.Dot(r, new Vector2(dNext.y, -dNext.x)));
                var o = r * (w * 0.5f * miter);
                if (i > 0) u += Vector2.Distance(pts[i], pts[i - 1]) / (w * 1.4f);
                v.Add(new Vector3(pts[i].x - o.x, y, pts[i].y - o.y)); uv.Add(new Vector2(u, 0f)); col.Add(Color.white);
                v.Add(new Vector3(pts[i].x + o.x, y, pts[i].y + o.y)); uv.Add(new Vector2(u, 1f)); col.Add(Color.white);
                if (i > 0) { int k = v.Count - 4; t.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        Texture2D ArrowTexture()
        {
            const int W = 64, H = 32;
            var tx = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float fy = Mathf.Abs((y + 0.5f) / H - 0.5f) * 2f;     // 0 centre .. 1 edge
                    float fx = (x + 0.5f) / W;
                    float chevron = Mathf.Abs(fx - 0.35f - 0.35f * (1f - fy));
                    bool arrow = chevron < 0.11f && fy < 0.8f;
                    float edge = fy > 0.86f ? 1f : 0f;
                    byte a = (byte)(arrow ? 255 : edge > 0f ? 240 : 175);
                    byte c = (byte)(arrow ? 255 : 210);
                    px[y * W + x] = new Color32(c, c, c, a);
                }
            tx.SetPixels32(px);
            tx.Apply(true);
            return tx;
        }

        void Ensure()
        {
            if (arrows == null) arrows = ArrowTexture();
            var sh = overlayShader != null ? overlayShader : Shader.Find("ThemePark/Overlay");
            if (primaryMat == null && sh != null)
            {
                primaryMat = new Material(sh) { hideFlags = HideFlags.DontSave };
                primaryMat.SetTexture("_BaseMap", arrows);
                primaryMat.SetFloat("_ScrollSpeed", 1.2f);
                secondaryMat = new Material(sh) { hideFlags = HideFlags.DontSave };
                secondaryMat.SetTexture("_BaseMap", arrows);
                secondaryMat.SetFloat("_ScrollSpeed", 0.8f);
                beaconMat = new Material(sh) { hideFlags = HideFlags.DontSave };
                ringMat = new Material(sh) { hideFlags = HideFlags.DontSave };
            }
            if (primaryMat != null) { primaryMat.SetColor("_BaseColor", primaryColor); secondaryMat.SetColor("_BaseColor", secondaryColor); }
            if (pinMat == null) { pinMat = Prims.NewLit("RoutePin", startColor, 0.5f, 0f); pinMat.hideFlags = HideFlags.DontSave; }
            if (cyl == null) cyl = ProcMesh.Cylinder(16);
            if (sph == null) sph = ProcMesh.Sphere(12, 8);
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (primary == null && secondary == null && start == null && beacon == null) return;
            Ensure();
            if (primaryMat == null) return;
            RenderParams P(Material m, bool shadows = false) => new RenderParams(m) { camera = cam, layer = gameObject.layer, shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = false };
            if (secondaryMesh != null) Graphics.RenderMesh(P(secondaryMat), secondaryMesh, 0, Matrix4x4.identity);
            if (primaryMesh != null) Graphics.RenderMesh(P(primaryMat), primaryMesh, 0, Matrix4x4.identity);
            if (start.HasValue)
            {
                var s = new Vector3(start.Value.x, 0f, start.Value.y);
                Graphics.RenderMesh(P(pinMat, true), cyl, 0, Matrix4x4.TRS(s, Quaternion.identity, new Vector3(0.25f, 3.2f, 0.25f)));
                Graphics.RenderMesh(P(pinMat, true), sph, 0, Matrix4x4.TRS(s + Vector3.up * 3.4f, Quaternion.identity, Vector3.one * 1.3f));
                ringMat.SetColor("_BaseColor", new Color(startColor.r, startColor.g, startColor.b, 0.55f));
                Graphics.RenderMesh(P(ringMat), cyl, 0, Matrix4x4.TRS(s + Vector3.up * 0.15f, Quaternion.identity, new Vector3(3.2f, 0.05f, 3.2f)));
            }
            if (beacon.HasValue)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin((Application.isPlaying ? Time.unscaledTime : 0f) * 3f);
                beaconMat.SetColor("_BaseColor", new Color(beaconColor.r, beaconColor.g, beaconColor.b, 0.18f + 0.12f * pulse));
                Graphics.RenderMesh(P(beaconMat), cyl, 0, Matrix4x4.TRS(beacon.Value, Quaternion.identity, new Vector3(5f, 60f, 5f)));
            }
        }
    }
}
