using System;
using System.Collections.Generic;

namespace PoleToolApp.Core
{
    public enum TAlign { Left, Center, Right }

    /// <summary>Item = id of the equipment item the entity belongs to (null for texts, dims, ground …).</summary>
    public abstract class DEnt { public Lyr Layer; public int Color; public string Item; }
    /// <summary>Polyline. Width in model units (0 = thin).</summary>
    public sealed class DLine : DEnt { public List<P2> Pts; public bool Closed; public WireKind Kind; public double Width; }
    /// <summary>Opaque mask polygon (wipeout in AutoCAD, white fill in the preview).</summary>
    public sealed class DMask : DEnt { public List<P2> Pts; }
    public sealed class DText : DEnt { public P2 Pos; public double H; public string S; public TAlign Align; public double Rot; public bool Boxed; }
    public sealed class DCircle : DEnt { public P2 C; public double R; public bool Filled; }
    /// <summary>Linear dimension between two points; vertical when Vertical, line placed at Offset (x or y).</summary>
    public sealed class DDim : DEnt { public P2 A, B; public bool Vertical; public double At; public string Override; }
    /// <summary>Table (BOM). Top-left corner, column widths, row height, cells [row][col].</summary>
    public sealed class DTable : DEnt { public P2 TopLeft; public double[] ColW; public double RowH, TextH; public List<string[]> Rows = new List<string[]>(); public string Title; }

    /// <summary>Leader with a name: arrow/dot at Anchor, line to Landing, underline to End, text above the underline.</summary>
    public sealed class DLeader : DEnt { public P2 Anchor, Landing, End; public string S; public double H; public int Side; }

    /// <summary>User block placed instead of generated geometry (Pos = projected centre of the item).</summary>
    public sealed class DInsert : DEnt { public string Name; public P2 Pos; public double Rot; }

    /// <summary>Area of a drawing that shows one view: x range, view direction and the shift applied to it.</summary>
    public sealed class ViewRegion { public double X0, X1; public View3 View; public P2 Offset; }

    public sealed class Drawing
    {
        public readonly List<DEnt> E = new List<DEnt>();
        public readonly List<ViewRegion> Views = new List<ViewRegion>();
        /// <summary>View region containing x (nearest one when between views).</summary>
        public ViewRegion RegionAt(double x)
        {
            ViewRegion best = null; double bd = double.MaxValue;
            foreach (var r in Views)
            {
                double dd = x < r.X0 ? r.X0 - x : x > r.X1 ? x - r.X1 : 0;
                if (dd < bd) { bd = dd; best = r; }
            }
            return best;
        }
        public void Line(Lyr l, params P2[] p) => E.Add(new DLine { Layer = l, Pts = new List<P2>(p) });
        public DText Text(Lyr l, P2 pos, double h, string s, TAlign a = TAlign.Left)
        { var t = new DText { Layer = l, Pos = pos, H = h, S = s, Align = a }; E.Add(t); return t; }

        public void Bounds(out double x0, out double y0, out double x1, out double y1)
        {
            double ax0 = double.MaxValue, ay0 = double.MaxValue, ax1 = double.MinValue, ay1 = double.MinValue;
            void A(P2 p) { if (p.X < ax0) ax0 = p.X; if (p.Y < ay0) ay0 = p.Y; if (p.X > ax1) ax1 = p.X; if (p.Y > ay1) ay1 = p.Y; }
            foreach (var e in E)
                switch (e)
                {
                    case DLine l: foreach (var p in l.Pts) A(p); break;
                    case DMask m: foreach (var p in m.Pts) A(p); break;
                    case DText t: A(t.Pos); A(new P2(t.Pos.X + (t.Align == TAlign.Right ? -1 : 1) * t.H * 0.8 * (t.S?.Length ?? 0), t.Pos.Y + t.H)); break;
                    case DInsert bi: A(bi.Pos); break;
                    case DLeader ld: A(ld.Anchor); A(ld.Landing); A(ld.End); A(new P2(ld.End.X, ld.End.Y + ld.H * 1.4)); break;
                    case DCircle c: A(new P2(c.C.X - c.R, c.C.Y - c.R)); A(new P2(c.C.X + c.R, c.C.Y + c.R)); break;
                    case DDim d: A(d.A); A(d.B); if (d.Vertical) A(new P2(d.At, d.A.Y)); else A(new P2(d.A.X, d.At)); break;
                    case DTable tb:
                        double w = 0; foreach (var c in tb.ColW) w += c;
                        A(tb.TopLeft); A(new P2(tb.TopLeft.X + w, tb.TopLeft.Y - tb.RowH * (tb.Rows.Count + 1))); break;
                }
            if (ax0 > ax1) { ax0 = ay0 = 0; ax1 = ay1 = 1; }
            x0 = ax0; y0 = ay0; x1 = ax1; y1 = ay1;
        }
    }

    /// <summary>Projects a scene into a 2D drawing with hidden parts masked (painter's algorithm per solid).</summary>
    public static class Projector
    {
        sealed class Piece { public string Item; public double Depth; public List<P2> Hull; public List<DLine> Lines = new List<DLine>(); public DInsert Insert; }

        /// <param name="sharpDeg">edges between faces bent more than this are drawn (smooth facets are not)</param>
        /// <param name="blockExists">when given, items with a user block for this view are emitted as DInsert</param>
        public static void Project(Scene scene, View3 view, P2 offset, Drawing d, bool masks = true, double sharpDeg = 25,
                                   Func<string, bool> blockExists = null)
        {
            var pieces = new List<Piece>();
            double cosSharp = Math.Cos(sharpDeg * Math.PI / 180);
            foreach (var item in scene.Items)
            {
                string blk = view == View3.Front ? item.BlockFront : view == View3.Side ? item.BlockSide : null;
                if (blk != null && blockExists != null && blockExists(blk) && item.Origin.HasValue)
                {
                    var pc = new Piece { Depth = view.Depth(item.Origin.Value) };
                    pieces.Add(pc); pc.Item = item.ItemId; pc.Insert = new DInsert { Layer = Lyr.Equip, Item = item.ItemId, Name = blk, Pos = view.P(item.Origin.Value) + offset };
                    continue;
                }
                foreach (var prim in item.Prims)
                {
                    if (prim is WireP w)
                    {
                        if (w.Pts.Count < 2) continue;
                        double dmin = double.MaxValue, dmax = double.MinValue;
                        foreach (var p in w.Pts) { double dd = view.Depth(p); dmin = Math.Min(dmin, dd); dmax = Math.Max(dmax, dd); }
                        if (dmax - dmin > 60)
                        {   // cable running toward / away from the viewer: sort short pieces on their own;
                            // key = nearer end + bias, so a cable lying on a surface is not covered by that surface
                            for (int k = 0; k + 1 < w.Pts.Count; k++)
                            {
                                var a = w.Pts[k]; var b = w.Pts[k + 1];
                                int n = (int)Math.Max(1, Math.Min(30, Math.Ceiling(Math.Abs(view.Depth(b) - view.Depth(a)) / 60)));
                                for (int q = 0; q < n; q++)
                                {
                                    var p0 = V3.Lerp(a, b, (double)q / n); var p1 = V3.Lerp(a, b, (double)(q + 1) / n);
                                    var sp = new Piece { Item = item.ItemId, Depth = Math.Max(view.Depth(p0), view.Depth(p1)) + 20 };
                                    sp.Lines.Add(new DLine { Layer = w.Layer, Color = w.Color, Pts = new List<P2> { view.P(p0) + offset, view.P(p1) + offset }, Kind = w.Kind, Width = w.Width });
                                    pieces.Add(sp);
                                }
                            }
                            continue;
                        }
                        var pc = new Piece { Item = item.ItemId }; double dep = 0;
                        var pts = new List<P2>(); foreach (var p in w.Pts) { pts.Add(view.P(p) + offset); dep += view.Depth(p); }
                        pc.Depth = dmax + 20;   // slightly in front: cables lying on a surface stay visible
                        pc.Lines.Add(new DLine { Layer = w.Layer, Color = w.Color, Pts = pts, Kind = w.Kind, Width = w.Width });
                        pieces.Add(pc); continue;
                    }
                    foreach (var part in Slices(prim, view))
                        AddSolid(pieces, item, part, view, offset, cosSharp);
                }
            }
            Finish(pieces, d, masks);
        }

        /// <summary>
        /// Long solids that run toward / away from the viewer (the pole in a view from above, pipes seen end-on …)
        /// are cut into slices, so each slice is depth-sorted on its own. Without this, a box in front of the lower
        /// pole could be sorted behind the pole's centre and disappear under its wipeout.
        /// </summary>
        const double step = 150;   // max. depth change per slice
        static int Cnt(V3 axis, double size) => (int)Math.Max(1, Math.Min(40, Math.Ceiling(Math.Abs(axis.Dot(View.Toward)) * size / step)));
        [ThreadStatic] static View3 View;

        static IEnumerable<Prim> Slices(Prim prim, View3 view)
        {
            View = view;
            if (prim is FrustumP f)
            {
                var ax = f.B - f.A; double len = ax.Len;
                int k = (int)Math.Min(40, Math.Ceiling(Math.Abs(ax.Unit.Dot(view.Toward)) * len / step));
                if (k <= 1) { yield return f; yield break; }
                for (int i = 0; i < k; i++)
                {
                    double t0 = (double)i / k, t1 = (double)(i + 1) / k;
                    yield return new FrustumP(V3.Lerp(f.A, f.B, t0), V3.Lerp(f.A, f.B, t1), f.R1 + (f.R2 - f.R1) * t0, f.R1 + (f.R2 - f.R1) * t1, f.Seg)
                    { Layer = f.Layer, Color = f.Color, CapA = i == 0 && f.CapA, CapB = i == k - 1 && f.CapB };
                }
                yield break;
            }
            if (prim is BoxP b)
            {
                // slice along every axis whose extent changes the depth a lot (at most 64 pieces)
                int kx = Cnt(b.Ax, b.Sx), ky = Cnt(b.Ay, b.Sy), kz = Cnt(b.Az, b.Sz);
                while (kx * ky * kz > 64) { if (kx >= ky && kx >= kz) kx = (kx + 1) / 2; else if (ky >= kz) ky = (ky + 1) / 2; else kz = (kz + 1) / 2; }
                if (kx * ky * kz <= 1) { yield return b; yield break; }
                for (int i = 0; i < kx; i++)
                    for (int j = 0; j < ky; j++)
                        for (int l = 0; l < kz; l++)
                        {
                            var c = b.C + b.Ax * (b.Sx * ((i + 0.5) / kx - 0.5)) + b.Ay * (b.Sy * ((j + 0.5) / ky - 0.5)) + b.Az * (b.Sz * ((l + 0.5) / kz - 0.5));
                            int cut = b.Cut;
                            if (i > 0) cut |= 1 << 4; if (i < kx - 1) cut |= 1 << 5;
                            if (j > 0) cut |= 1 << 2; if (j < ky - 1) cut |= 1 << 3;
                            if (l > 0) cut |= 1 << 0; if (l < kz - 1) cut |= 1 << 1;
                            yield return new BoxP(c, b.Sx / kx, b.Sy / ky, b.Sz / kz) { Ax = b.Ax, Ay = b.Ay, Az = b.Az, Layer = b.Layer, Color = b.Color, Cut = cut };
                        }
                yield break;
            }
            if (prim is PrismP pr)
            {
                int k = (int)Math.Min(40, Math.Ceiling(Math.Abs(pr.E.Unit.Dot(view.Toward)) * pr.E.Len / step));
                if (k <= 1) { yield return pr; yield break; }
                for (int i = 0; i < k; i++)
                    yield return new PrismP(pr.O + pr.E * ((double)i / k), pr.U, pr.V, pr.E * (1.0 / k), pr.Profile)
                    { Layer = pr.Layer, Color = pr.Color, CapB = i == 0 && pr.CapB, CapT = i == k - 1 && pr.CapT };
                yield break;
            }
            yield return prim;
        }

        static void AddSolid(List<Piece> pieces, SceneItem item, Prim prim, View3 view, P2 offset, double cosSharp)
        {
                    var m = prim.ToMesh(); if (m == null || m.F.Count == 0) return;
                    var piece = new Piece { Item = item.ItemId, Depth = view.Depth(m.Center) };
                    var proj = new List<P2>(m.V.Count); foreach (var p in m.V) proj.Add(view.P(p) + offset);
                    piece.Hull = Hull.Of(proj);
                    if (piece.Hull.Count < 3 || Hull.Area(piece.Hull) < 1e-6) piece.Hull = null;
                    // edges -> adjacent faces
                    var normals = new List<V3>(m.F.Count); var front = new List<bool>(m.F.Count);
                    foreach (var f in m.F) { var n = m.Normal(f); normals.Add(n); front.Add(n.Dot(view.Toward) > 1e-7); }
                    var edges = new Dictionary<long, (int a, int b, int f1, int f2)>();
                    for (int fi = 0; fi < m.F.Count; fi++)
                    {
                        var f = m.F[fi];
                        for (int i = 0; i < f.Length; i++)
                        {
                            int a = f[i], b = f[(i + 1) % f.Length]; long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                            if (edges.TryGetValue(key, out var e)) edges[key] = (e.a, e.b, e.f1, fi); else edges[key] = (a, b, fi, -1);
                        }
                    }
                    foreach (var e in edges.Values)
                    {
                        if (m.Virtual.Contains(e.f1) || (e.f2 >= 0 && m.Virtual.Contains(e.f2))) continue;
                        bool draw;
                        if (e.f2 < 0) draw = front[e.f1];
                        else if (front[e.f1] != front[e.f2]) draw = true;   // silhouette
                        else draw = front[e.f1] && normals[e.f1].Dot(normals[e.f2]) < cosSharp;
                        if (!draw) continue;
                        var p1 = proj[e.a]; var p2 = proj[e.b];
                        if ((p1 - p2).Len < 1e-6) continue;
                        piece.Lines.Add(new DLine { Layer = prim.Layer, Color = prim.Color, Pts = new List<P2> { p1, p2 } });
                    }
                    pieces.Add(piece);
        }

        static void Finish(List<Piece> pieces, Drawing d, bool masks)
        {
            pieces.Sort((a, b) => a.Depth.CompareTo(b.Depth));   // far first
            foreach (var pc in pieces)
            {
                if (pc.Insert != null) { d.E.Add(pc.Insert); continue; }
                if (masks && pc.Hull != null) d.E.Add(new DMask { Layer = Lyr.Equip, Pts = pc.Hull, Item = pc.Item });
                foreach (var l in pc.Lines) { l.Item = pc.Item; d.E.Add(l); }
            }
        }

        /// <summary>Joins collinear touching 2-point segments of the same layer into longer lines (fewer entities).</summary>
        public static int MergeSegments(Drawing d, double tol = 0.05)
        {
            var keep = new List<DEnt>(); var segs = new Dictionary<(Lyr, int, long), List<DLine>>();
            foreach (var e in d.E)
            {
                if (e is DLine l && l.Pts.Count == 2 && !l.Closed && l.Width == 0 && l.Kind == WireKind.Solid)
                {
                    var a = l.Pts[0]; var b = l.Pts[1];
                    double ang = Math.Atan2(b.Y - a.Y, b.X - a.X); if (ang < 0) ang += Math.PI; if (ang >= Math.PI - 1e-9) ang = 0;
                    // line id: angle + distance of the infinite line from origin
                    double nx = -Math.Sin(ang), ny = Math.Cos(ang); double dist = a.X * nx + a.Y * ny;
                    long key = (long)Math.Round(ang * 1e4) * 10000019L + (long)Math.Round(dist / tol);
                    var k = (l.Layer, l.Color, key);
                    if (!segs.TryGetValue(k, out var list)) segs[k] = list = new List<DLine>();
                    list.Add(l); keep.Add(null);   // placeholder keeps relative order roughly
                    keep[keep.Count - 1] = l;
                }
                else keep.Add(e);
            }
            int removed = 0;
            var drop = new HashSet<DLine>();
            foreach (var list in segs.Values)
            {
                if (list.Count < 2) continue;
                var a0 = list[0].Pts[0]; var b0 = list[0].Pts[1]; var dir = new P2(b0.X - a0.X, b0.Y - a0.Y); var len = dir.Len; dir = dir * (1 / len);
                double T(P2 p) => (p.X - a0.X) * dir.X + (p.Y - a0.Y) * dir.Y;
                var iv = new List<(double s, double e, DLine l)>();
                foreach (var l in list) { double s = T(l.Pts[0]), e = T(l.Pts[1]); if (s > e) (s, e) = (e, s); iv.Add((s, e, l)); }
                iv.Sort((x, y) => x.s.CompareTo(y.s));
                var cur = iv[0];
                for (int i = 1; i <= iv.Count; i++)
                {
                    if (i < iv.Count && iv[i].s <= cur.e + tol) { if (iv[i].e > cur.e) cur.e = iv[i].e; drop.Add(iv[i].l); removed++; continue; }
                    cur.l.Pts[0] = new P2(a0.X + dir.X * cur.s, a0.Y + dir.Y * cur.s);
                    cur.l.Pts[1] = new P2(a0.X + dir.X * cur.e, a0.Y + dir.Y * cur.e);
                    if (i < iv.Count) cur = iv[i];
                }
            }
            if (removed > 0) { d.E.Clear(); foreach (var e in keep) if (!(e is DLine l && drop.Contains(l))) d.E.Add(e); }
            return removed;
        }
    }
}
