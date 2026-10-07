using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThemePark
{
    public enum ColorBy { State, Type, Guide }

    /// <summary>Draws every visible visitor with GPU instancing (one draw list per colour bucket). Works in edit and play mode.</summary>
    [ExecuteAlways]
    public class VisitorRenderer : MonoBehaviour
    {
        public SimRunner runner;
        public ColorBy colorBy = ColorBy.State;
        public int selected = -1;
        public bool castShadows = true;

        public static readonly Color[] StateColors =
        {
            new Color(0.16f, 0.49f, 0.88f),   // walking
            new Color(0.95f, 0.61f, 0.07f),   // queueing
            new Color(0.15f, 0.68f, 0.38f),   // eating
            new Color(0.09f, 0.63f, 0.52f),   // resting
            new Color(0.50f, 0.55f, 0.56f),   // leaving
            new Color(0.80f, 0.55f, 0.95f),   // browsing (shops, photo spots)
        };
        public static readonly Color[] TypeColors =
        {
            new Color(0.95f, 0.77f, 0.06f),   // family
            new Color(0.61f, 0.35f, 0.71f),   // teen
            new Color(0.20f, 0.60f, 0.86f),   // adult
        };
        public static readonly Color[] GuideColors =
        {
            new Color(0.10f, 0.85f, 0.85f),   // guided (app)
            new Color(0.85f, 0.85f, 0.85f),   // not guided
        };
        public static readonly Color Highlight = new Color(1f, 0.15f, 0.75f);

        const int Buckets = 6;
        Material[] mats;
        Material highlightMat;
        Mesh mesh;
        readonly List<Matrix4x4>[] lists = new List<Matrix4x4>[Buckets];
        Matrix4x4[] chunk = new Matrix4x4[1023];
        Matrix4x4 selectedM;
        bool hasSelected;
        int builtFrame = -1;
        ParkSim.VisibleFn collect;
        public int LastDrawn { get; private set; }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            for (int i = 0; i < Buckets; i++) lists[i] = new List<Matrix4x4>(1024);
            collect = Collect;
        }

        void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; }

        Color BucketColor(int b)
        {
            switch (colorBy)
            {
                case ColorBy.Type: return b < TypeColors.Length ? TypeColors[b] : Color.white;
                case ColorBy.Guide: return b < GuideColors.Length ? GuideColors[b] : Color.white;
                default: return b < StateColors.Length ? StateColors[b] : Color.white;
            }
        }

        void EnsureResources()
        {
            if (mesh == null) mesh = ProcMesh.Person();
            if (mats == null || mats.Length != Buckets || mats[0] == null)
            {
                mats = new Material[Buckets];
                for (int i = 0; i < Buckets; i++) { mats[i] = Prims.NewLit("Visitor" + i, Color.white, 0.25f, 0f); mats[i].hideFlags = HideFlags.DontSave; }
                highlightMat = Prims.NewLit("VisitorSelected", Highlight, 0.4f, 0f);
                highlightMat.hideFlags = HideFlags.DontSave;
            }
            for (int i = 0; i < Buckets; i++) Prims.SetColor(mats[i], BucketColor(i));
        }

        int Bucket(Visitor v)
        {
            switch (colorBy)
            {
                case ColorBy.Type: return (int)v.type;
                case ColorBy.Guide: return v.guided ? 0 : 1;
                default:
                    switch (v.state)
                    {
                        case VState.Queue: return 1;
                        case VState.Eating: return 2;
                        case VState.Resting: return 3;
                        case VState.Browsing: return 5;
                        default: return v.goal == Goal.Exit ? 4 : 0;
                    }
            }
        }

        void Collect(Visitor v, Vector2 p, Vector2 f)
        {
            uint h = unchecked((uint)v.id * 2654435761u);
            float s = 0.94f + 0.12f * ((h >> 24) / 255f);
            var rot = f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(new Vector3(f.x, 0f, f.y)) : Quaternion.identity;
            if (v.id == selected)
            {
                selectedM = Matrix4x4.TRS(new Vector3(p.x, 0.02f, p.y), rot, Vector3.one * 1.45f);
                hasSelected = true;
                return;
            }
            lists[Bucket(v)].Add(Matrix4x4.TRS(new Vector3(p.x, 0.02f, p.y), rot, new Vector3(s, s, s)));
        }

        void Rebuild()
        {
            if (builtFrame == Time.frameCount && Application.isPlaying) return;
            builtFrame = Time.frameCount;
            for (int i = 0; i < Buckets; i++) lists[i].Clear();
            hasSelected = false;
            var sim = runner != null ? runner.Sim : null;
            if (sim == null) return;
            sim.ForEachVisible(collect);
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (runner == null || runner.Sim == null) { LastDrawn = 0; return; }
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            EnsureResources();
            Rebuild();
            int drawn = 0;
            for (int b = 0; b < Buckets; b++)
            {
                var list = lists[b];
                if (list.Count == 0) continue;
                var rp = new RenderParams(mats[b])
                {
                    camera = cam, layer = gameObject.layer, receiveShadows = true,
                    shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 2000f)
                };
                for (int start = 0; start < list.Count; start += 1023)
                {
                    int n = Mathf.Min(1023, list.Count - start);
                    list.CopyTo(start, chunk, 0, n);
                    Graphics.RenderMeshInstanced(rp, mesh, 0, chunk, n);
                    drawn += n;
                }
            }
            if (hasSelected)
            {
                var rp = new RenderParams(highlightMat) { camera = cam, layer = gameObject.layer, worldBounds = new Bounds(Vector3.zero, Vector3.one * 2000f) };
                Graphics.RenderMesh(rp, mesh, 0, selectedM);
                drawn++;
            }
            LastDrawn = drawn;
        }
    }
}
