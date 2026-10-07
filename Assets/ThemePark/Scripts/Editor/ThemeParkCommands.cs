using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ThemePark.UI;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ThemePark.EditorTools
{
    /// <summary>Material provider that persists materials as assets (so the saved scene references real files).</summary>
    public class AssetMaterialProvider : IMaterialProvider
    {
        public const string Folder = "Assets/ThemePark/Materials";
        readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public Material Get(string key, Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            if (cache.TryGetValue(key, out var c) && c != null) return c;
            EnsureFolder(Folder);
            string path = $"{Folder}/{key}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = Prims.NewLit(key, color, smoothness, metallic); AssetDatabase.CreateAsset(m, path); }
            else
            {
                if (m.shader != Prims.LitShader) m.shader = Prims.LitShader;
                Prims.SetColor(m, color);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            cache[key] = m;
            return m;
        }

        public Material Unlit(string key, Color color) => Shader(key, "Universal Render Pipeline/Unlit", color);

        public Material Shader(string key, string shaderName, Color color)
        {
            if (cache.TryGetValue(key, out var c) && c != null) return c;
            EnsureFolder(Folder);
            string path = $"{Folder}/{key}.mat";
            var sh = shaderName.StartsWith("Assets/") ? AssetDatabase.LoadAssetAtPath<Shader>(shaderName) : UnityEngine.Shader.Find(shaderName);
            if (sh == null) sh = Prims.LitShader;
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh) { name = key }; AssetDatabase.CreateAsset(m, path); }
            else m.shader = sh;
            Prims.SetColor(m, color);
            EditorUtility.SetDirty(m);
            cache[key] = m;
            return m;
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>Saves generated meshes as assets under Assets/ThemePark/Generated (overwriting in place).</summary>
    public class AssetMeshStore : IMeshStore
    {
        public const string Folder = "Assets/ThemePark/Generated";
        public Mesh Save(string key, Mesh mesh)
        {
            AssetMaterialProvider.EnsureFolder(Folder);
            string path = $"{Folder}/{key}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == mesh && existing != null) return existing;
            mesh.name = key;
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = key;
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }

    /// <summary>CLI commands for the theme-park simulator: `unity command tp_*`.</summary>
    public static class ThemeParkCommands
    {
        const string ScenePath = "Assets/ThemePark/Scenes/ThemePark.unity";
        const string OverlayShaderPath = "Assets/ThemePark/Shaders/Overlay.shader";
        const string UiFolder = "Assets/ThemePark/UI";
        const string PanelSettingsPath = UiFolder + "/ThemeParkPanelSettings.asset";
        const string ThemePath = UiFolder + "/ThemeParkTheme.tss";
        const string TextSettingsPath = UiFolder + "/ThemeParkTextSettings.asset";
        const string FontPath = UiFolder + "/Fonts/NotoSansKR-Regular.otf";
        const string FontAssetPath = UiFolder + "/Fonts/NotoSansKR-Regular SDF.asset";
        const string VolumePath = "Assets/ThemePark/Settings/ThemeParkVolume.asset";
        const string SkyPath = "Assets/ThemePark/Materials/Sky.mat";

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        static string Clock(float t) { int s = Mathf.RoundToInt(t); return $"{s / 3600:00}:{s / 60 % 60:00}"; }
        static string Mins(float s) => F1(s / 60f) + "min";

        static float ParseClock(string s, float fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            var p = s.Split(':');
            if (p.Length >= 2 && int.TryParse(p[0], out var h) && int.TryParse(p[1], out var m)) return h * 3600f + m * 60f;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)) return hours * 3600f;
            return fallback;
        }

        static SimParams Params(int visitors, int seed, float guided)
        {
            var p = new SimParams();
            if (visitors > 0) p.dailyVisitors = visitors;
            if (seed > 0) p.seed = seed;
            if (guided >= 0f) p.guidedShare = Mathf.Clamp01(guided);
            return p;
        }

        // ------------------------------------------------------------------ scene

        [CliCommand("tp_setup_scene", "Create the ThemePark scene: park scenery and rides, visitors, overlays, route guide, camera and UI. Saves the scene.")]
        public static string SetupScene(
            [CliArg("visitors", "Visitors per day")] int visitors = 8000,
            [CliArg("scene_path", "Scene asset path")] string scenePath = ScenePath)
        {
            if (Application.isPlaying) return "error: exit play mode first";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var mats = new AssetMaterialProvider();
            var meshes = new AssetMeshStore();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sunGo.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = Sky();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.68f, 0.76f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.62f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.40f, 0.30f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.85f, 0.92f);
            RenderSettings.fogStartDistance = 350f;
            RenderSettings.fogEndDistance = 1400f;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) { urp.shadowDistance = 450f; EditorUtility.SetDirty(urp); }

            var runnerGo = new GameObject("Simulation");
            var runner = runnerGo.AddComponent<SimRunner>();
            runner.parameters.dailyVisitors = visitors;
            var layout = runner.EnsureLayout();

            var parkRoot = new GameObject("Park").transform;
            var anims = ParkBuilder.Build(layout, parkRoot, mats, meshes);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(OverlayShaderPath);
            var vis = runnerGo.AddComponent<VisitorRenderer>(); vis.runner = runner;
            var ov = runnerGo.AddComponent<OverlayRenderer>(); ov.runner = runner; ov.overlayShader = shader;
            var rv = runnerGo.AddComponent<RouteView>(); rv.overlayShader = shader;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2500f;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rig = camGo.AddComponent<CameraRig>();
            rig.parkBounds = layout.bounds;
            rig.FrameOverview();
            EnsureVolume();

            var appGo = new GameObject("App");
            var app = appGo.AddComponent<AppController>();
            app.runner = runner; app.visitorsView = vis; app.overlay = ov; app.routeView = rv; app.cameraRig = rig;
            app.parkRoot = parkRoot; app.animators = anims.ToArray();
            EnsureUI(app);
            PlayerSettings.runInBackground = true;

            AssetMaterialProvider.EnsureFolder(Path.GetDirectoryName(scenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, scenePath);
            AddSceneToBuild(scenePath);
            AssetDatabase.SaveAssets();
            int objects = UnityEngine.Object.FindObjectsByType<Transform>().Length;
            return $"scene={scenePath} objects={objects} rides={layout.Rides.Count()} pois={layout.pois.Count} nodes={layout.nodes.Count} edges={layout.edges.Count} " +
                   $"paths={F1(layout.TotalPathLength)}m animators={anims.Count} build_ms={sw.ElapsedMilliseconds}";
        }

        static Material Sky()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (m != null) return m;
            var sh = Shader.Find("Skybox/Procedural");
            if (sh == null) return null;
            AssetMaterialProvider.EnsureFolder(Path.GetDirectoryName(SkyPath).Replace('\\', '/'));
            m = new Material(sh) { name = "Sky" };
            if (m.HasProperty("_SkyTint")) m.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.85f));
            if (m.HasProperty("_AtmosphereThickness")) m.SetFloat("_AtmosphereThickness", 0.85f);
            if (m.HasProperty("_Exposure")) m.SetFloat("_Exposure", 1.2f);
            if (m.HasProperty("_SunSize")) m.SetFloat("_SunSize", 0.03f);
            AssetDatabase.CreateAsset(m, SkyPath);
            return m;
        }

        static void AddSceneToBuild(string path)
        {
            var list = EditorBuildSettings.scenes.Where(s => s.path != path && !s.path.EndsWith("SampleScene.unity")).ToList();
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void EnsureVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (profile == null)
            {
                AssetMaterialProvider.EnsureFolder(Path.GetDirectoryName(VolumePath).Replace('\\', '/'));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumePath);
                var bloom = profile.Add<Bloom>(true);
                bloom.threshold.Override(1.1f);
                bloom.intensity.Override(0.35f);
                var tone = profile.Add<Tonemapping>(true);
                tone.mode.Override(TonemappingMode.Neutral);
                var col = profile.Add<ColorAdjustments>(true);
                col.saturation.Override(12f);
                col.postExposure.Override(0.1f);
                foreach (var c in profile.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, profile); }
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            var go = new GameObject("PostProcess");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        static AppController App(out string error)
        {
            error = null;
            var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
            if (app == null) error = "error: no AppController in the active scene (tp_setup_scene)";
            return app;
        }

        // ------------------------------------------------------------------ analysis (headless, no scene changes)

        [CliCommand("tp_park_info", "Describe the park layout: zones, walkways, attractions with capacity.")]
        public static string ParkInfo()
        {
            var L = ParkPresets.Default();
            var sb = new StringBuilder();
            sb.AppendLine($"park={L.name} bounds={L.bounds} nodes={L.nodes.Count} edges={L.edges.Count} paths={F1(L.TotalPathLength)}m");
            foreach (var z in L.zones) sb.AppendLine($"zone {z.id} {z.name}: walkable {F1(z.area)} m2");
            float cap = 0f;
            foreach (var p in L.pois)
            {
                if (p.kind == PoiKind.Ride) cap += p.Throughput;
                sb.AppendLine($"{p.kind} {p.name} [{p.model}] zone={L.zones[p.zone].name} entrance=({F(p.entrance.x)},{F(p.entrance.y)}) pos=({F1(p.pos.x)},{F1(p.pos.y)}) size={p.size} " +
                              (p.kind == PoiKind.Ride ? $"batch={p.batch} interval={p.interval}s duration={p.duration}s throughput={F1(p.Throughput)}/h pop={p.popularity} thrill={p.thrill} queue_slots={QueueLayout.Capacity(p)}"
                               : p.kind == PoiKind.Food ? $"seats={p.capacity} service={p.serviceMin / 60f}-{p.serviceMax / 60f}min" : ""));
            }
            sb.AppendLine($"total ride capacity={F1(cap)} riders/h");
            // overlap check: POI footprints vs walkways and each other
            int issues = 0;
            foreach (var p in L.pois)
            {
                if (p.kind == PoiKind.Gate) continue;
                foreach (var e in L.edges)
                {
                    if (L.nodes[e.b].name == "@" + p.name || L.nodes[e.a].name == "@" + p.name) continue;
                    Vector2 a = L.nodes[e.a].pos, b = L.nodes[e.b].pos;
                    for (int k = 0; k <= 20; k++)
                        if (p.Contains(Vector2.Lerp(a, b, k / 20f), e.width * 0.5f)) { sb.AppendLine($"OVERLAP {p.name} with walkway {L.nodes[e.a].name}-{L.nodes[e.b].name}"); issues++; break; }
                }
                foreach (var q in L.pois)
                    if (q.id > p.id && q.kind != PoiKind.Gate && (p.Contains(q.pos, 0f) || q.Contains(p.pos, 0f))) { sb.AppendLine($"OVERLAP {p.name} with {q.name}"); issues++; }
            }
            sb.AppendLine($"layout_issues={issues}");
            return sb.ToString();
        }

        [CliCommand("tp_sim_run", "Run a whole park day headless and report statistics, ETA accuracy and consistency checks.")]
        public static string SimRun(
            [CliArg("visitors", "Visitors per day")] int visitors = 8000,
            [CliArg("seed", "Random seed")] int seed = 1,
            [CliArg("guided", "Share of visitors using the guide app (0-1)")] float guided = 0.5f,
            [CliArg("until", "Stop clock HH:MM (default: until everyone left)")] string until = "",
            [CliArg("export", "Write CSV/JSON to Exports/")] bool export = false,
            [CliArg("detail", "Per-ride/zone/edge detail")] bool detail = true)
        {
            var L = ParkPresets.Default();
            var sim = new ParkSim(L, Params(visitors, seed, guided));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float end = ParseClock(until, L.closeTime + 3f * 3600f);
            while (sim.t < end && !(sim.Finished && sim.t > L.closeTime + 60f)) sim.Step();
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            var r = Report(sim, ms, detail);
            if (export)
            {
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Exports", $"run_v{visitors}_s{seed}_g{F(guided)}"));
                r += $"\nexported {sim.tel.Export(folder).Count} files to {folder}";
            }
            return r;
        }

        public static string Report(ParkSim sim, float ms, bool detail)
        {
            var L = sim.L; var tel = sim.tel;
            var sb = new StringBuilder();
            sim.RecountNow();
            int negEdges = sim.edgeCount.Count(c => c < 0);
            int queued = sim.poi.Sum(s => s.queue.Count);
            int stuckWalking = sim.visitors.Count(v => v.state == VState.Walking && v.edge < 0);
            sb.AppendLine($"clock={Clock(sim.t)} steps={sim.steps} run_ms={F1(ms)} visitors/day={sim.DailyVisitors} guided={F(sim.P.guidedShare)} seed={sim.P.seed}");
            sb.AppendLine($"entered={sim.entered} exited={sim.exited} in_park={sim.inPark} peak={tel.peakInPark}@{Clock(tel.peakInParkT)} conservation={(sim.entered == sim.exited + sim.inPark ? "ok" : "BROKEN")} " +
                          $"neg_edge_counts={negEdges} queued_now={queued} walking_without_edge={stuckWalking}");
            sb.AppendLine($"now: walking={sim.nWalking} queue={sim.nQueue} riding={sim.nRiding} eating={sim.nEating} resting={sim.nResting} browsing={sim.nBrowsing}");
            sb.AppendLine($"rides={tel.totalRides} per_visitor={F1(tel.totalRides / Mathf.Max(1f, sim.entered))} avg_wait={Mins(tel.AvgRideWait())} avg_distance={F1(tel.AvgDistanceAll())}m " +
                          $"avg_exit_distance={F1(tel.AvgExitDistance())}m hist(0.5km)=[{string.Join(",", tel.DistanceHistogram())}]");
            foreach (var (name, filt) in new[] { ("all", -1), ("app", 1), ("no_app", 0) })
            {
                var e = tel.Eta(filt);
                sb.AppendLine($"eta[{name}] trips={e.n} walk_mae={F1(e.walkMae)}s walk_bias={F1(e.walkBias)}s wait_mae_posted={Mins(e.waitMaeNaive)} wait_mae_forecast={Mins(e.waitMaeForecast)} " +
                              $"total_mae_forecast={Mins(e.totalMaeForecast)} total_mape={F1(e.totalMapeForecast * 100f)}%");
            }
            if (!detail) return sb.ToString();
            float openH = Mathf.Max(0.01f, (Mathf.Min(sim.t, L.closeTime) - L.openTime) / 3600f);
            foreach (var st in sim.poi)
            {
                var p = st.p;
                if (p.kind == PoiKind.Gate) continue;
                if (p.kind == PoiKind.Ride)
                {
                    float maxQ = tel.queueLen[p.id].Count > 0 ? tel.queueLen[p.id].Max() : 0f;
                    float maxPosted = tel.postedWait[p.id].Count > 0 ? tel.postedWait[p.id].Max() : 0f;
                    sb.AppendLine($"  ride {p.name}: riders={st.riders} util={F1(100f * st.riders / (p.Throughput * openH))}% avg_wait={Mins(st.AvgWait)} max_wait={Mins(st.waitMax)} " +
                                  $"max_queue={maxQ} max_posted={F1(maxPosted)}min by_hour=[{string.Join(",", tel.ridersByHour[p.id])}]");
                }
                else sb.AppendLine($"  {p.kind.ToString().ToLower()} {p.name}: guests={st.riders} avg_wait={Mins(st.AvgWait)} inside_now={st.inside}");
            }
            foreach (var z in L.zones)
                sb.AppendLine($"  zone {z.name}: area={F1(z.area)}m2 peak_density={F(tel.zonePeak[z.id])}p/m2 @{Clock(tel.zonePeakT[z.id])} LOS={Walk.LosNames[Walk.Los(tel.zonePeak[z.id])]}");
            var top = Enumerable.Range(0, L.edges.Count).OrderByDescending(i => tel.edgeFlow[i]).Take(6);
            foreach (var i in top)
            {
                var e = L.edges[i];
                sb.AppendLine($"  edge {L.nodes[e.a].name}-{L.nodes[e.b].name} w={e.width}m traversals={tel.edgeFlow[i]} peak_density={F(tel.edgePeak[i])}@{Clock(tel.edgePeakT[i])} LOS={Walk.LosNames[Walk.Los(tel.edgePeak[i])]}");
            }
            return sb.ToString();
        }

        [CliCommand("tp_sim_compare", "Compare whole days with different guide-app shares (e.g. 0,0.5,1).")]
        public static string SimCompare(
            [CliArg("visitors", "Visitors per day")] int visitors = 8000,
            [CliArg("guided", "Comma-separated shares")] string guided = "0,0.5,1",
            [CliArg("seed", "Random seed")] int seed = 1)
        {
            var sb = new StringBuilder();
            foreach (var g in guided.Split(','))
            {
                float share = float.Parse(g, CultureInfo.InvariantCulture);
                var L = ParkPresets.Default();
                var sim = new ParkSim(L, Params(visitors, seed, share));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!(sim.Finished && sim.t > L.closeTime + 60f) && sim.t < L.closeTime + 3f * 3600f) sim.Step();
                var e = sim.tel.Eta();
                sb.AppendLine($"guided={F(share)} rides={sim.tel.totalRides} per_visitor={F1(sim.tel.totalRides / Mathf.Max(1f, sim.entered))} avg_wait={Mins(sim.tel.AvgRideWait())} " +
                              $"avg_distance={F1(sim.tel.AvgDistanceAll())}m peak={sim.tel.peakInPark} wait_mae_posted={Mins(e.waitMaeNaive)} wait_mae_forecast={Mins(e.waitMaeForecast)} run_ms={sw.ElapsedMilliseconds}");
            }
            return sb.ToString();
        }

        [CliCommand("tp_route", "Route guide (headless or on the scene's running simulation): from a point / gate to an attraction.")]
        public static string Route(
            [CliArg("to", "Attraction name")] string to,
            [CliArg("from", "x,z or 'gate'")] string from = "gate",
            [CliArg("at", "Run a headless day up to HH:MM first (empty = use the scene simulation if any, else an empty park)")] string at = "")
        {
            ParkLayout L; ParkSim sim = null;
            if (!string.IsNullOrEmpty(at))
            {
                L = ParkPresets.Default();
                sim = new ParkSim(L, new SimParams());
                sim.RunUntil(ParseClock(at, L.openTime));
            }
            else
            {
                var app = UnityEngine.Object.FindAnyObjectByType<AppController>();
                sim = app != null ? app.Sim : null;
                L = sim != null ? sim.L : ParkPresets.Default();
            }
            var poi = L.Find(to);
            if (poi == null) return $"error: no attraction '{to}'. Names: {string.Join(", ", L.pois.Select(p => p.name))}";
            Vector2 start = from == "gate" ? L.nodes[L.gateNode].pos + new Vector2(0f, 6f) : ParseXZ(from);
            var sb = new StringBuilder();
            sb.AppendLine($"from=({F1(start.x)},{F1(start.y)}) to={poi.name} clock={(sim != null ? Clock(sim.t) : "empty park")}");
            foreach (var mode in new[] { RouteMode.Fastest, RouteMode.Shortest })
            {
                var g = RouteGuide.Query(L, sim, start, poi.id, mode);
                if (!g.ok) { sb.AppendLine($"{mode}: no route"); continue; }
                var names = new List<string>();
                foreach (var ei in g.edges) { var e = L.edges[ei]; names.Add($"{L.nodes[e.a].name}-{L.nodes[e.b].name}"); }
                sb.AppendLine($"{mode}: length={F1(g.length)}m walk={F1(g.walkTime)}s (free {F1(g.freeWalkTime)}s) wait={Mins(g.wait)} (posted {Mins(g.waitNow)}) total={Mins(g.Total)} " +
                              $"arrive={Clock(g.arriveClock)} board={Clock(g.boardClock)} worst_LOS={Walk.LosNames[g.worstLos]} edges=[{string.Join(" ", names)}]");
            }
            return sb.ToString();
        }

        static Vector2 ParseXZ(string s)
        {
            var p = s.Split(',').Select(x => float.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray();
            return new Vector2(p[0], p[1]);
        }

        // ------------------------------------------------------------------ preview / view (edit or play mode)

        [CliCommand("tp_preview", "Edit or play mode: (re)run the scene simulation up to a clock time and show it (visitors, rides, overlays).")]
        public static string Preview(
            [CliArg("at", "Clock HH:MM")] string at = "12:00",
            [CliArg("visitors", "Visitors per day (0 = keep)")] int visitors = 0,
            [CliArg("guided", "Guide-app share (-1 = keep)")] float guided = -1f,
            [CliArg("reset", "Start a new day first")] bool reset = true)
        {
            var app = App(out var e); if (app == null) return e;
            var r = app.runner;
            if (visitors > 0) r.parameters.dailyVisitors = visitors;
            if (guided >= 0f) r.parameters.guidedShare = Mathf.Clamp01(guided);
            if (reset || r.Sim == null) r.ResetSim();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float target = ParseClock(at, r.Sim.L.openTime + 3600f);
            if (target > r.Sim.t) r.Advance(target - r.Sim.t);
            r.Sim.RecountNow();
            app.AnimateRides();
            if (app.overlay != null) app.overlay.Refresh(true);
            app.RefreshGuide();
            SceneView.RepaintAll();
            return Status() + $" advance_ms={sw.ElapsedMilliseconds}";
        }

        [CliCommand("tp_view", "Switch view: guest (3D orbit) | control (top-down map + dashboard, heat map, walkway LOS).")]
        public static string View([CliArg("mode", "guest | control")] string mode = "guest")
        {
            var app = App(out var e); if (app == null) return e;
            app.SetView(mode.StartsWith("c") ? ViewMode.Control : ViewMode.Guest);
            return $"view={app.view} camera={app.cameraRig.mode} heat={app.overlay.heat} edges={app.overlay.edges}";
        }

        [CliCommand("tp_camera", "Camera: orbit | map | follow | overview, with yaw/pitch/distance/pivot.")]
        public static string CameraCmd([CliArg("mode", "orbit | map | follow | overview")] string mode = "overview",
            [CliArg("yaw", "deg (NaN keeps)")] float yaw = float.NaN, [CliArg("pitch", "deg (NaN keeps)")] float pitch = float.NaN,
            [CliArg("distance", "m (<=0 keeps); map: half height")] float distance = 0f, [CliArg("pivot", "x,z (empty keeps)")] string pivot = "")
        {
            var app = App(out var e); if (app == null) return e;
            var rig = app.cameraRig;
            if (mode == "overview") rig.FrameOverview();
            else if (mode == "map") rig.FrameMap();
            else if (mode == "follow") rig.SetMode(CameraRig.Mode.Follow);
            else rig.mode = CameraRig.Mode.Orbit;
            if (!float.IsNaN(yaw)) rig.yaw = yaw;
            if (!float.IsNaN(pitch)) rig.pitch = pitch;
            if (distance > 0f) { if (rig.mode == CameraRig.Mode.Map) rig.mapSize = distance; else if (rig.mode == CameraRig.Mode.Follow) rig.followDistance = distance; else rig.distance = distance; }
            if (!string.IsNullOrEmpty(pivot)) { var p = ParseXZ(pivot); rig.pivot = new Vector3(p.x, 0f, p.y); }
            rig.Place();
            return $"camera mode={rig.mode} pos={rig.transform.position} yaw={F(rig.yaw)} pitch={F(rig.pitch)} dist={F(rig.distance)} map={F(rig.mapSize)}";
        }

        [CliCommand("tp_display", "Display options: color (state|type|guide), heat (off|live|cum), edges (off|los|flow).")]
        public static string Display([CliArg("color", "state | type | guide (empty keeps)")] string color = "",
            [CliArg("heat", "off | live | cum (empty keeps)")] string heat = "", [CliArg("edges", "off | los | flow (empty keeps)")] string edges = "")
        {
            var app = App(out var e); if (app == null) return e;
            if (color.StartsWith("s")) app.visitorsView.colorBy = ColorBy.State;
            else if (color.StartsWith("t")) app.visitorsView.colorBy = ColorBy.Type;
            else if (color.StartsWith("g")) app.visitorsView.colorBy = ColorBy.Guide;
            if (heat.StartsWith("o")) app.overlay.heat = HeatMode.Off;
            else if (heat.StartsWith("l")) app.overlay.heat = HeatMode.Live;
            else if (heat.StartsWith("c")) app.overlay.heat = HeatMode.Cumulative;
            if (edges.StartsWith("o")) app.overlay.edges = EdgeMode.Off;
            else if (edges.StartsWith("l")) app.overlay.edges = EdgeMode.Density;
            else if (edges.StartsWith("f")) app.overlay.edges = EdgeMode.Flow;
            app.overlay.Refresh(true);
            SceneView.RepaintAll();
            return $"color={app.visitorsView.colorBy} heat={app.overlay.heat} edges={app.overlay.edges}";
        }

        [CliCommand("tp_guide", "Route guide in the scene: --from gate | x,z | visitor:<id> | random, --to <attraction> (empty clears).")]
        public static string Guide([CliArg("to", "Attraction name (empty = clear)")] string to = "", [CliArg("from", "gate | x,z | visitor:<id> | random")] string from = "gate")
        {
            var app = App(out var e); if (app == null) return e;
            var L = app.Layout;
            if (string.IsNullOrEmpty(to)) { app.ClearGuide(); return "guide cleared"; }
            var poi = L.Find(to);
            if (poi == null) return $"error: no attraction '{to}'";
            if (from == "gate") app.SetStartGate();
            else if (from == "random") { int id = app.PickWalkingVisitor(7); if (id < 0 || !app.SetStartVisitor(id)) return "error: no walking visitor"; }
            else if (from.StartsWith("visitor:")) { if (!app.SetStartVisitor(int.Parse(from.Substring(8)))) return "error: visitor not active"; }
            else app.SetStartPoint(ParseXZ(from));
            app.SetDestination(poi.id);
            SceneView.RepaintAll();
            var f = app.fastest; var s = app.shortest;
            string R(string n, GuideResult g) => g == null || !g.ok ? $"{n}: none" :
                $"{n}: {F1(g.length)}m walk={F1(g.walkTime)}s wait={Mins(g.wait)} total={Mins(g.Total)} board={Clock(g.boardClock)} LOS={Walk.LosNames[g.worstLos]}";
            var v = app.SelectedVisitor;
            return $"start={(v != null ? $"visitor {v.id} ({v.state}, {v.type})" : app.startPoint.ToString())} to={poi.name} same_route={(f != null && s != null && AppController.SameRoute(f, s))} | {R("fastest", f)} | {R("shortest", s)}";
        }

        // ------------------------------------------------------------------ play mode

        static AppController PlayApp(out string error)
        {
            error = null;
            if (!Application.isPlaying) { error = "error: enter play mode first (editor_play)"; return null; }
            return App(out error);
        }

        [CliCommand("tp_sim_start", "Play mode: start (or resume) the day.")]
        public static string SimStart([CliArg("speed", "Speed multiplier (<=0 keeps)")] float speed = 0f)
        {
            var app = PlayApp(out var e); if (app == null) return e;
            if (speed > 0f) app.runner.speed = speed;
            app.runner.Play();
            return Status();
        }

        [CliCommand("tp_sim_pause", "Play mode: pause.")]
        public static string SimPause()
        {
            var app = PlayApp(out var e); if (app == null) return e;
            app.runner.Pause();
            return Status();
        }

        [CliCommand("tp_sim_reset", "Play mode: start a new day (optionally with other visitor count / guide share).")]
        public static string SimReset([CliArg("visitors", "Visitors per day (0 keeps)")] int visitors = 0, [CliArg("guided", "Guide share (-1 keeps)")] float guided = -1f)
        {
            var app = PlayApp(out var e); if (app == null) return e;
            if (visitors > 0) app.runner.parameters.dailyVisitors = visitors;
            if (guided >= 0f) app.runner.parameters.guidedShare = Mathf.Clamp01(guided);
            app.runner.ResetSim();
            return Status();
        }

        [CliCommand("tp_sim_speed", "Play mode: speed multiplier (1-600).")]
        public static string SimSpeed([CliArg("x", "Speed multiplier")] float x)
        {
            var app = PlayApp(out var e); if (app == null) return e;
            app.runner.speed = Mathf.Clamp(x, 1f, 600f);
            return Status();
        }

        [CliCommand("tp_sim_advance", "Advance N simulated seconds immediately (play or edit mode).")]
        public static string SimAdvance([CliArg("seconds", "Simulated seconds")] float seconds = 600f)
        {
            var app = App(out var e); if (app == null) return e;
            app.runner.Advance(seconds);
            app.AnimateRides();
            if (app.overlay != null) app.overlay.Refresh(true);
            app.RefreshGuide();
            return Status();
        }

        [CliCommand("tp_sim_status", "Live status of the scene simulation.")]
        public static string Status()
        {
            var app = App(out var e); if (app == null) return e;
            var s = app.Sim;
            if (s == null) return "no simulation (tp_preview or play mode)";
            s.RecountNow();
            var waits = string.Join(" ", s.poi.Where(p => p.p.kind == PoiKind.Ride).OrderByDescending(p => p.WaitNow(s.t)).Take(4).Select(p => $"{p.p.name}={Mins(p.WaitNow(s.t))}"));
            return $"clock={Clock(s.t)} running={app.runner.running} speed={F(app.runner.speed)}x in_park={s.inPark} entered={s.entered} walking={s.nWalking} queue={s.nQueue} riding={s.nRiding} " +
                   $"eating={s.nEating} resting={s.nResting} rides={s.tel.totalRides} avg_wait={Mins(s.tel.AvgRideWait())} drawn={app.visitorsView.LastDrawn} step_ms={F1(app.runner.LastStepMs)} top_waits: {waits}";
        }

        [CliCommand("tp_export", "Export the scene simulation's data (CSV/JSON) to Exports/.")]
        public static string Export([CliArg("name", "Folder name under Exports/")] string name = "")
        {
            var app = App(out var e); if (app == null) return e;
            if (app.Sim == null) return "error: no simulation";
            if (string.IsNullOrEmpty(name)) name = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Exports", name));
            var files = app.Sim.tel.Export(folder);
            return $"{files.Count} files → {folder}: {string.Join(", ", files.Select(Path.GetFileName))}";
        }

        // ------------------------------------------------------------------ UI

        static PanelSettings EnsurePanelSettings()
        {
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (ps != null) return ps;
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            return ps;
        }

        static void EnsureUI(AppController app)
        {
            var ui = UnityEngine.Object.FindAnyObjectByType<ThemeParkUI>();
            if (ui == null)
            {
                var go = new GameObject("UI");
                go.AddComponent<UIDocument>();
                ui = go.AddComponent<ThemeParkUI>();
            }
            var doc = ui.GetComponent<UIDocument>();
            doc.panelSettings = EnsurePanelSettings();
            ui.app = app;
            SetupFont(false);
            EditorUtility.SetDirty(doc);
            EditorUtility.SetDirty(ui);
        }

        [CliCommand("tp_setup_font", "Create the Noto Sans KR dynamic font asset and register it as a fallback for the UI.")]
        public static string SetupFont([CliArg("force", "Recreate the font asset")] bool force = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) return $"error: font not found: {FontPath}";
            var fa = AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath);
            if (fa != null && force) { AssetDatabase.DeleteAsset(FontAssetPath); fa = null; }
            if (fa == null)
            {
                fa = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (fa == null) return "error: CreateFontAsset failed";
                fa.name = "NotoSansKR-Regular SDF";
                fa.isMultiAtlasTexturesEnabled = true;
                AssetDatabase.CreateAsset(fa, FontAssetPath);
                if (fa.atlasTextures != null)
                    foreach (var tex in fa.atlasTextures)
                        if (tex != null) { tex.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(tex, fa); }
                if (fa.material != null) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
                EditorUtility.SetDirty(fa);
            }
            var ts = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (ts == null) { ts = ScriptableObject.CreateInstance<PanelTextSettings>(); AssetDatabase.CreateAsset(ts, TextSettingsPath); }
            if (ts.fallbackFontAssets == null) ts.fallbackFontAssets = new List<FontAsset>();
            if (!ts.fallbackFontAssets.Contains(fa)) ts.fallbackFontAssets.Add(fa);
            ts.clearDynamicDataOnBuild = true;
            EditorUtility.SetDirty(ts);
            var ps = EnsurePanelSettings();
            ps.textSettings = ts;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return $"font={FontAssetPath} hangul={fa.HasCharacter('한', true, true)}";
        }
    }
}
