using System;
using System.Collections.Generic;
using System.Linq;
using PoleToolApp.Core;

/// <summary>
/// Compares the painter's-algorithm drawing with an exact z-buffer render of the same scene, for many view
/// directions. Reports items that are wrongly hidden (visible in reality, covered in the drawing) or wrongly shown.
/// Solids are compared by pixel coverage, cables by sampled points.
/// </summary>
static class VisCheck
{
    const int W = 500;

    public sealed class Result { public string Item, Name; public double Az, El, HiddenFrac, ShownFrac; public int Px; public bool Wire; }

    public static List<Result> Run(Scene scene, double az, double el)
    {
        var view = new View3(az, el);
        // ---- bounds
        var all = new List<V3>();
        foreach (var it in scene.Items) foreach (var p in it.Prims)
        { if (p is WireP wp) all.AddRange(wp.Pts); else { var m = p.ToMesh(); if (m != null) all.AddRange(m.V); } }
        double x0 = all.Min(p => view.P(p).X), x1 = all.Max(p => view.P(p).X), y0 = all.Min(p => view.P(p).Y), y1 = all.Max(p => view.P(p).Y);
        double s = (W - 4) / Math.Max(x1 - x0, y1 - y0);
        int w = (int)((x1 - x0) * s) + 4, h = (int)((y1 - y0) * s) + 4;
        (double, double) Px(P2 p) => ((p.X - x0) * s + 2, (y1 - p.Y) * s + 2);

        // ---- exact z-buffer
        var ids = scene.Items.Select(i => i.ItemId).Distinct().ToList();
        var idx = ids.Select((v, i) => (v, i)).ToDictionary(t => t.v, t => t.i);
        var zb = new double[w * h]; var zi = new int[w * h];
        for (int i = 0; i < zb.Length; i++) { zb[i] = double.MinValue; zi[i] = -1; }
        foreach (var it in scene.Items)
            foreach (var p in it.Prims)
            {
                if (p is WireP) continue;
                var m = p.ToMesh(); if (m == null) continue;
                foreach (var f in m.F)
                    for (int k = 1; k + 1 < f.Length; k++)
                        Tri(m.V[f[0]], m.V[f[k]], m.V[f[k + 1]], view, Px, w, h, (x, y, d) =>
                        { int o = y * w + x; if (d > zb[o]) { zb[o] = d; zi[o] = idx[it.ItemId]; } });
            }

        // ---- painter result (masks in drawing order)
        var d2 = new Drawing(); Projector.Project(scene, view, new P2(0, 0), d2, true);
        var pi = new int[w * h]; for (int i = 0; i < pi.Length; i++) pi[i] = -1;
        var order = new int[w * h];   // index of the last mask written per pixel
        for (int e = 0; e < d2.E.Count; e++)
            if (d2.E[e] is DMask mk && mk.Item != null)
            {
                int id = idx[mk.Item]; int ee = e;
                Poly(mk.Pts.Select(Px).ToList(), w, h, (x, y) => { pi[y * w + x] = id; order[y * w + x] = ee; });
            }

        var res = new List<Result>();
        // ---- solids: compare coverage per item (ignore the outline band: 1 px erosion)
        var trueVis = new int[ids.Count]; var wrongHid = new int[ids.Count]; var wrongShown = new int[ids.Count]; var paintVis = new int[ids.Count];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int o = y * w + x; int t = zi[o], p = pi[o];
                if (t != zi[o - 1] || t != zi[o + 1] || t != zi[o - w] || t != zi[o + w]) continue;   // edge pixel
                if (t >= 0) { trueVis[t]++; if (p != t) wrongHid[t]++; }
                if (p >= 0) { paintVis[p]++; if (t != p && t >= 0 && zi[o] >= 0) wrongShown[p]++; }
            }
        for (int i = 0; i < ids.Count; i++)
            if (trueVis[i] > 15)
            {
                var it = scene.Items.First(q => q.ItemId == ids[i]);
                res.Add(new Result { Item = ids[i], Name = it.En, Az = az, El = el, Px = trueVis[i], HiddenFrac = (double)wrongHid[i] / trueVis[i], ShownFrac = paintVis[i] == 0 ? 0 : (double)wrongShown[i] / paintVis[i] });
            }

        // ---- cables: sample points; truly visible if nothing solid is closer than the wire point
        var lineIdx = new Dictionary<DLine, int>(); for (int e = 0; e < d2.E.Count; e++) if (d2.E[e] is DLine dl) lineIdx[dl] = e;
        foreach (var it in scene.Items)
        {
            int tv = 0, wh = 0;
            foreach (var p in it.Prims.OfType<WireP>())
                for (int k = 0; k + 1 < p.Pts.Count; k++)
                    for (int q = 1; q < 8; q++)
                    {
                        var pt = V3.Lerp(p.Pts[k], p.Pts[k + 1], q / 8.0);
                        var (fx, fy) = Px(view.P(pt)); int x = (int)fx, y = (int)fy; if (x < 1 || y < 1 || x >= w - 1 || y >= h - 1) continue;
                        double dep = view.Depth(pt);
                        // real visibility: no solid in a 3x3 neighbourhood clearly in front (tolerance 40 mm)
                        bool vis = true;
                        for (int yy = -1; yy <= 1 && vis; yy++) for (int xx = -1; xx <= 1; xx++) { int o = (y + yy) * w + x + xx; if (zb[o] > dep + 40) { vis = false; break; } }
                        if (!vis) continue;
                        tv++;
                        // painter: covered if the last mask on that pixel comes after this wire's line
                        var line = FindLine(d2, it.ItemId, pt, view);
                        int o0 = y * w + x;
                        if (line >= 0 && pi[o0] >= 0 && order[o0] > line && zi[o0] >= 0 && zb[o0] < dep + 40) wh++;
                    }
            if (tv > 10) res.Add(new Result { Item = it.ItemId, Name = it.En, Az = az, El = el, Px = tv, HiddenFrac = (double)wh / tv, Wire = true });
        }
        return res;
    }

    /// <summary>Share of sampled points on solid outlines of a cleaned drawing that are really hidden (should be ~0).</summary>
    public static double HiddenLineFraction(Scene scene, Drawing d, double az, double el)
    {
        var view = new View3(az, el);
        var all = new List<V3>();
        foreach (var it in scene.Items) foreach (var p in it.Prims) { var m = p.ToMesh(); if (m != null) all.AddRange(m.V); }
        double x0 = all.Min(p => view.P(p).X), x1 = all.Max(p => view.P(p).X), y0 = all.Min(p => view.P(p).Y), y1 = all.Max(p => view.P(p).Y);
        double s = 1200 / Math.Max(x1 - x0, y1 - y0); int w = (int)((x1 - x0) * s) + 4, h = (int)((y1 - y0) * s) + 4;
        (double, double) Px(P2 p) => ((p.X - x0) * s + 2, (y1 - p.Y) * s + 2);
        var zb = new double[w * h]; for (int i = 0; i < zb.Length; i++) zb[i] = double.MinValue;
        foreach (var it in scene.Items) foreach (var p in it.Prims)
        {
            if (p is WireP) continue; var m = p.ToMesh(); if (m == null) continue;
            foreach (var f in m.F) for (int k = 1; k + 1 < f.Length; k++)
                Tri(m.V[f[0]], m.V[f[k]], m.V[f[k + 1]], view, Px, w, h, (x, y, dd) => { int o = y * w + x; if (dd > zb[o]) zb[o] = dd; });
        }
        // a sample is "really hidden" when the whole 5x5 neighbourhood is solid surface (line not on an outline) –
        // detected as: surface everywhere around AND the sample lies clearly inside one solid's area
        int tot = 0, bad = 0;
        foreach (var e in d.E.OfType<DLine>())
        {
            if (e.Layer == Lyr.Cable || e.Layer == Lyr.Earth || e.Layer == Lyr.Center || e.Layer == Lyr.Guy) continue;
            for (int k = 0; k + 1 < e.Pts.Count; k++)
                for (int q = 1; q < 4; q++)
                {
                    var pt = new P2(e.Pts[k].X + (e.Pts[k + 1].X - e.Pts[k].X) * q / 4, e.Pts[k].Y + (e.Pts[k + 1].Y - e.Pts[k].Y) * q / 4);
                    var (fx, fy) = Px(pt); int x = (int)fx, y = (int)fy; if (x < 3 || y < 3 || x >= w - 3 || y >= h - 3) continue;
                    tot++;
                    // smoothness test: depth varies little around -> sample lies on the inside of one visible face
                    double c = zb[y * w + x]; if (c == double.MinValue) continue;
                    bool flat = true;
                    for (int yy = -3; yy <= 3 && flat; yy++) for (int xx = -3; xx <= 3; xx++)
                    { double z = zb[(y + yy) * w + x + xx]; if (z == double.MinValue || Math.Abs(z - c) > 25) { flat = false; break; } }
                    if (flat) bad++;
                }
        }
        return tot == 0 ? 0 : (double)bad / tot;
    }

    /// <summary>Drawing index of the line of 'item' passing nearest to the projected point.</summary>
    static int FindLine(Drawing d, string item, V3 pt, View3 view)
    {
        var p = view.P(pt); int best = -1; double bd = 30;
        for (int e = 0; e < d.E.Count; e++)
            if (d.E[e] is DLine l && l.Item == item)
                for (int k = 0; k + 1 < l.Pts.Count; k++)
                {
                    double dd = Seg(l.Pts[k], l.Pts[k + 1], p); if (dd < bd) { bd = dd; best = e; }
                }
        return best;
    }

    static double Seg(P2 a, P2 b, P2 p)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
        double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y; return Math.Sqrt(x * x + y * y);
    }

    static void Tri(V3 a, V3 b, V3 c, View3 v, Func<P2, (double, double)> px, int w, int h, Action<int, int, double> plot)
    {
        var (ax, ay) = px(v.P(a)); var (bx, by) = px(v.P(b)); var (cx, cy) = px(v.P(c));
        double da = v.Depth(a), db = v.Depth(b), dc = v.Depth(c);
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), maxX = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), maxY = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
        double area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax); if (Math.Abs(area) < 1e-9) return;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double px0 = x + 0.5, py0 = y + 0.5;
                double w0 = ((bx - px0) * (cy - py0) - (by - py0) * (cx - px0)) / area;
                double w1 = ((cx - px0) * (ay - py0) - (cy - py0) * (ax - px0)) / area;
                double w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                plot(x, y, w0 * da + w1 * db + w2 * dc);
            }
    }

    static void Poly(List<(double x, double y)> p, int w, int h, Action<int, int> plot)
    {
        if (p.Count < 3) return;
        int minY = Math.Max(0, (int)p.Min(q => q.y)), maxY = Math.Min(h - 1, (int)Math.Ceiling(p.Max(q => q.y)));
        var xs = new List<double>();
        for (int y = minY; y <= maxY; y++)
        {
            double yy = y + 0.5; xs.Clear();
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
                if ((p[i].y > yy) != (p[j].y > yy)) xs.Add(p[i].x + (yy - p[i].y) * (p[j].x - p[i].x) / (p[j].y - p[i].y));
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                for (int x = Math.Max(0, (int)Math.Ceiling(xs[k] - 0.5)); x <= Math.Min(w - 1, (int)Math.Floor(xs[k + 1] - 0.5)); x++) plot(x, y);
        }
    }
}
