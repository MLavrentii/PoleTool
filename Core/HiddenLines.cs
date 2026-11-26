using System;
using System.Collections.Generic;
using System.Linq;

namespace PoleToolApp.Core
{
    /// <summary>
    /// Turns a painter's-algorithm drawing (lines + opaque masks in drawing order) into a clean line drawing:
    /// every line part covered by a LATER mask is cut away, masks are dropped, then duplicate / overlapping
    /// collinear lines are merged (overkill). Masks are convex (hulls), so each mask hides one interval per segment.
    /// </summary>
    public static class HiddenLines
    {
        sealed class M { public int Index; public List<P2> Pts; public double X0, Y0, X1, Y1; }

        public static void Remove(Drawing d, double eps = 0.3)
        {
            // ---- masks + grid index
            var masks = new List<M>();
            for (int i = 0; i < d.E.Count; i++)
                if (d.E[i] is DMask dm && dm.Pts.Count >= 3)
                {
                    var m = new M { Index = i, Pts = dm.Pts, X0 = dm.Pts.Min(p => p.X), X1 = dm.Pts.Max(p => p.X), Y0 = dm.Pts.Min(p => p.Y), Y1 = dm.Pts.Max(p => p.Y) };
                    masks.Add(m);
                }
            var result = new List<DEnt>();
            if (masks.Count == 0) { d.E.RemoveAll(e => e is DMask); return; }
            double gx0 = masks.Min(m => m.X0), gy0 = masks.Min(m => m.Y0), gx1 = masks.Max(m => m.X1), gy1 = masks.Max(m => m.Y1);
            double cell = Math.Max(50, Math.Max(gx1 - gx0, gy1 - gy0) / 120);
            int nx = (int)((gx1 - gx0) / cell) + 1, ny = (int)((gy1 - gy0) / cell) + 1;
            var grid = new List<M>[nx * ny];
            foreach (var m in masks)
                for (int cx = Cx(m.X0); cx <= Cx(m.X1); cx++)
                    for (int cy = Cy(m.Y0); cy <= Cy(m.Y1); cy++)
                        (grid[cy * nx + cx] ??= new List<M>()).Add(m);
            int Cx(double x) => Math.Max(0, Math.Min(nx - 1, (int)((x - gx0) / cell)));
            int Cy(double y) => Math.Max(0, Math.Min(ny - 1, (int)((y - gy0) / cell)));

            var cand = new HashSet<M>();
            for (int i = 0; i < d.E.Count; i++)
            {
                var e = d.E[i];
                if (e is DMask) continue;
                if (!(e is DLine l) || l.Pts.Count < 2) { result.Add(e); continue; }
                var outLines = new List<List<P2>>(); List<P2> cur = null;
                int nseg = l.Closed ? l.Pts.Count : l.Pts.Count - 1;
                for (int k = 0; k < nseg; k++)
                {
                    var a = l.Pts[k]; var b = l.Pts[(k + 1) % l.Pts.Count];
                    // candidate masks drawn after this line
                    cand.Clear();
                    for (int cx = Cx(Math.Min(a.X, b.X)); cx <= Cx(Math.Max(a.X, b.X)); cx++)
                        for (int cy = Cy(Math.Min(a.Y, b.Y)); cy <= Cy(Math.Max(a.Y, b.Y)); cy++)
                        {
                            var g = grid[cy * nx + cx]; if (g == null) continue;
                            foreach (var m in g) if (m.Index > i) cand.Add(m);
                        }
                    var vis = new List<(double s, double t)> { (0, 1) };
                    foreach (var m in cand)
                    {
                        if (Math.Max(a.X, b.X) < m.X0 || Math.Min(a.X, b.X) > m.X1 || Math.Max(a.Y, b.Y) < m.Y0 || Math.Min(a.Y, b.Y) > m.Y1) continue;
                        if (!Clip(m.Pts, a, b, eps, out double t0, out double t1)) continue;
                        vis = Subtract(vis, t0, t1);
                        if (vis.Count == 0) break;
                    }
                    double len = (b - a).Len;
                    foreach (var (s, t) in vis)
                    {
                        if ((t - s) * len < 0.05) continue;
                        var p0 = a + (b - a) * s; var p1 = a + (b - a) * t;
                        if (cur != null && s < 1e-9 && (cur[cur.Count - 1] - p0).Len < 1e-6) cur.Add(p1);
                        else { cur = new List<P2> { p0, p1 }; outLines.Add(cur); }
                    }
                    if (vis.Count == 0 || vis[vis.Count - 1].t < 1 - 1e-9) cur = null;
                }
                foreach (var pts in outLines)
                    result.Add(new DLine { Layer = l.Layer, Color = l.Color, Item = l.Item, Kind = l.Kind, Width = l.Width, Pts = pts });
            }
            d.E.Clear(); d.E.AddRange(result);
        }

        /// <summary>Cyrus–Beck: parameter interval of segment a-b strictly inside the convex polygon (shrunk by eps).</summary>
        static bool Clip(List<P2> poly, P2 a, P2 b, double eps, out double t0, out double t1)
        {
            t0 = 0; t1 = 1;
            // orientation of the polygon
            double area = 0; for (int i = 0; i < poly.Count; i++) { var p = poly[i]; var q = poly[(i + 1) % poly.Count]; area += p.X * q.Y - q.X * p.Y; }
            double sgn = area >= 0 ? 1 : -1;
            var dv = b - a;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                double ex = q.X - p.X, ey = q.Y - p.Y, el = Math.Sqrt(ex * ex + ey * ey); if (el < 1e-12) continue;
                double nx = -ey / el * sgn, ny = ex / el * sgn;   // inward unit normal
                double num = nx * (a.X - p.X) + ny * (a.Y - p.Y) - eps;
                double den = nx * dv.X + ny * dv.Y;
                if (Math.Abs(den) < 1e-12) { if (num < 0) return false; continue; }
                double t = -num / den;
                if (den > 0) { if (t > t0) t0 = t; } else { if (t < t1) t1 = t; }
                if (t0 >= t1) return false;
            }
            return t1 - t0 > 1e-9;
        }

        static List<(double s, double t)> Subtract(List<(double s, double t)> vis, double h0, double h1)
        {
            var r = new List<(double, double)>(vis.Count + 1);
            foreach (var (s, t) in vis)
            {
                if (h1 <= s || h0 >= t) { r.Add((s, t)); continue; }
                if (h0 > s) r.Add((s, h0));
                if (h1 < t) r.Add((h1, t));
            }
            return r;
        }

        /// <summary>Hidden-line removal + overkill (merge duplicate / overlapping collinear lines, drop specks).</summary>
        public static void Clean(Drawing d)
        {
            Remove(d);
            Projector.MergeSegments(d, 0.2);
            d.E.RemoveAll(e => e is DLine l && l.Pts.Count == 2 && (l.Pts[1] - l.Pts[0]).Len < 0.3);
        }
    }
}
