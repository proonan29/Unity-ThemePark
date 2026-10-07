using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ThemePark.UI
{
    /// <summary>Minimal line chart drawn with Painter2D (time on X in hours of day).</summary>
    public class LineChart : VisualElement
    {
        public struct Series { public List<float> y; public Color color; public string name; }
        public readonly List<Series> series = new List<Series>();
        public List<float> x;
        public float xMin = 9f, xMax = 22f, yMax;
        public float now = -1f;                      // vertical "now" line (hours)
        readonly List<Label> labels = new List<Label>();
        public readonly VisualElement legend;        // add this below the chart
        const float L = 34f, R = 6f, T = 6f, B = 16f;
        static readonly Color Grid = new Color(1f, 1f, 1f, 0.08f), Axis = new Color(1f, 1f, 1f, 0.35f);

        public LineChart(float height)
        {
            style.height = height;
            style.marginBottom = 2;
            generateVisualContent += Draw;
            legend = new VisualElement();
            legend.style.flexDirection = FlexDirection.Row;
            legend.style.flexWrap = Wrap.Wrap;
            legend.style.marginLeft = L;
            legend.style.marginBottom = 4;
        }

        public void SetData(List<float> xs, float yMaxHint = 0f)
        {
            x = xs;
            float m = yMaxHint;
            foreach (var s in series) if (s.y != null) foreach (var v in s.y) if (v > m) m = v;
            yMax = NiceMax(m);
            MarkDirtyRepaint();
            UpdateLabels();
        }

        public static float NiceMax(float m)
        {
            if (m <= 0f) return 1f;
            float p = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(m)));
            foreach (var k in new[] { 1f, 2f, 2.5f, 5f, 10f }) if (m <= k * p) return k * p;
            return 10f * p;
        }

        Label Lab(int i)
        {
            while (labels.Count <= i)
            {
                var l = new Label();
                l.style.position = Position.Absolute;
                l.style.fontSize = 9;
                l.style.color = new Color(0.7f, 0.72f, 0.76f);
                l.pickingMode = PickingMode.Ignore;
                labels.Add(l); Add(l);
            }
            return labels[i];
        }

        void UpdateLabels()
        {
            float h = resolvedStyle.height;
            if (float.IsNaN(h) || h <= 0f) h = style.height.value.value;
            float plotH = h - B - T;
            int k = 0;
            for (int i = 0; i <= 2; i++)
            {
                var l = Lab(k++);
                float v = yMax * i / 2f;
                l.text = v >= 1000f ? (v / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k" : v.ToString("0.#", CultureInfo.InvariantCulture);
                l.style.left = 0; l.style.width = L - 4; l.style.unityTextAlign = TextAnchor.MiddleRight;
                l.style.top = T + plotH * (1f - i / 2f) - 7f;
            }
            float w = resolvedStyle.width; if (float.IsNaN(w) || w <= 0f) w = 300f;
            for (float hh = Mathf.Ceil(xMin / 3f) * 3f; hh <= xMax; hh += 3f)
            {
                var l = Lab(k++);
                l.text = $"{(int)hh:00}:00";
                l.style.unityTextAlign = TextAnchor.UpperCenter;
                l.style.width = 40;
                l.style.left = L + (w - L - R) * (hh - xMin) / (xMax - xMin) - 20f;
                l.style.top = T + plotH + 1f;
            }
            for (int i = k; i < labels.Count; i++) labels[i].text = "";
            legend.Clear();
            foreach (var s in series)
            {
                if (string.IsNullOrEmpty(s.name)) continue;
                var l = new Label($"<color=#{ColorUtility.ToHtmlStringRGB(s.color)}>━</color> {s.name}");
                l.enableRichText = true;
                l.style.fontSize = 10; l.style.color = new Color(0.82f, 0.84f, 0.88f);
                l.style.marginRight = 8;
                legend.Add(l);
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w < 20f || h < 20f) return;
            var p = ctx.painter2D;
            float x0 = L, x1 = w - R, y0 = T, y1 = h - B;
            p.lineWidth = 1f;
            p.strokeColor = Grid;
            for (int i = 0; i <= 2; i++) { float y = Mathf.Lerp(y1, y0, i / 2f); p.BeginPath(); p.MoveTo(new Vector2(x0, y)); p.LineTo(new Vector2(x1, y)); p.Stroke(); }
            for (float hh = Mathf.Ceil(xMin / 3f) * 3f; hh <= xMax; hh += 3f)
            {
                float xx = Mathf.Lerp(x0, x1, (hh - xMin) / (xMax - xMin));
                p.BeginPath(); p.MoveTo(new Vector2(xx, y0)); p.LineTo(new Vector2(xx, y1)); p.Stroke();
            }
            p.strokeColor = Axis;
            p.BeginPath(); p.MoveTo(new Vector2(x0, y1)); p.LineTo(new Vector2(x1, y1)); p.Stroke();
            if (x == null || x.Count < 2 || yMax <= 0f) return;
            foreach (var s in series)
            {
                if (s.y == null || s.y.Count < 2) continue;
                p.strokeColor = s.color;
                p.lineWidth = 1.6f;
                p.lineJoin = LineJoin.Round;
                p.BeginPath();
                int n = Mathf.Min(x.Count, s.y.Count);
                int stride = Mathf.Max(1, n / 400);
                for (int i = 0; i < n; i += stride)
                {
                    var pt = new Vector2(Mathf.Lerp(x0, x1, (x[i] - xMin) / (xMax - xMin)), Mathf.Lerp(y1, y0, Mathf.Clamp01(s.y[i] / yMax)));
                    if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                }
                p.Stroke();
            }
            if (now >= xMin && now <= xMax)
            {
                float xx = Mathf.Lerp(x0, x1, (now - xMin) / (xMax - xMin));
                p.strokeColor = new Color(1f, 1f, 1f, 0.5f); p.lineWidth = 1f;
                p.BeginPath(); p.MoveTo(new Vector2(xx, y0)); p.LineTo(new Vector2(xx, y1)); p.Stroke();
            }
        }
    }

    /// <summary>Simple vertical bar chart with category labels.</summary>
    public class BarChart : VisualElement
    {
        public float[] values = new float[0];
        public string[] names = new string[0];
        public Color color = new Color(0.3f, 0.7f, 1f);
        readonly List<Label> labels = new List<Label>();
        const float B = 14f, T = 12f;

        public BarChart(float height)
        {
            style.height = height;
            style.marginBottom = 4;
            generateVisualContent += Draw;
        }

        public void SetData(float[] v, string[] n)
        {
            values = v; names = n;
            MarkDirtyRepaint();
            float w = resolvedStyle.width; if (float.IsNaN(w) || w <= 0f) w = 300f;
            float max = 1f; foreach (var x in values) max = Mathf.Max(max, x);
            int count = values.Length;
            while (labels.Count < count * 2)
            {
                var l = new Label(); l.style.position = Position.Absolute; l.style.fontSize = 9;
                l.style.color = new Color(0.7f, 0.72f, 0.76f); l.style.unityTextAlign = TextAnchor.UpperCenter; l.pickingMode = PickingMode.Ignore;
                labels.Add(l); Add(l);
            }
            float h = resolvedStyle.height; if (float.IsNaN(h) || h <= 0f) h = style.height.value.value;
            float bw = w / Mathf.Max(1, count);
            for (int i = 0; i < labels.Count; i++) labels[i].text = "";
            for (int i = 0; i < count; i++)
            {
                var a = labels[2 * i]; a.text = names != null && i < names.Length ? names[i] : "";
                a.style.left = i * bw - 6f; a.style.width = bw + 12f; a.style.top = h - B + 1f;
                var b = labels[2 * i + 1]; b.text = values[i] > 0 ? values[i].ToString("0", CultureInfo.InvariantCulture) : "";
                b.style.left = i * bw - 6f; b.style.width = bw + 12f;
                b.style.top = Mathf.Lerp(h - B, T, values[i] / max) - 12f;
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (values == null || values.Length == 0 || r.width < 10f) return;
            float max = 1f; foreach (var x in values) max = Mathf.Max(max, x);
            var p = ctx.painter2D;
            float bw = r.width / values.Length;
            p.fillColor = color;
            for (int i = 0; i < values.Length; i++)
            {
                float hh = (r.height - B - T) * values[i] / max;
                float x0 = i * bw + bw * 0.15f, x1 = (i + 1) * bw - bw * 0.15f, y1 = r.height - B, y0 = y1 - hh;
                if (hh < 0.5f) continue;
                p.BeginPath();
                p.MoveTo(new Vector2(x0, y1)); p.LineTo(new Vector2(x0, y0)); p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, y1)); p.ClosePath();
                p.Fill();
            }
        }
    }
}
