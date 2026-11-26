using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PoleToolApp.Core
{
    /// <summary>Composes the complete drawing: views, ground line, labels, dimensions, captions, BOM table.</summary>
    public static class Sheet
    {
        public sealed class BomRow { public int No; public string Name, Spec, Qty, Remark; public List<string> ItemIds = new List<string>(); }

        static readonly HashSet<string> FarDefs = new HashSet<string> { "svc_line", "guy", "guy_ins", "guy_guard", "guy_anchor", "guy_grip", "ug_pipe", "handhole", "hv_cable", "earth_rod", "earth_a", "earth_d" };

        public static Drawing Compose(PoleJob job, Scene scene, out List<BomRow> bom, Func<string, bool> blockExists = null)
        {
            bool jp = job.LabelsJp;
            double th = job.TextPaper * job.Scale;
            var d = new Drawing();
            bom = BuildBom(job, scene);
            var views = new List<(View3 v, string jp, string en)>();
            var front = Math.Abs(job.ElevRot) < 1e-9 ? View3.Front : new View3(job.ElevRot, 0);
            var side = Math.Abs(job.ElevRot) < 1e-9 ? View3.Side : new View3(job.ElevRot - 90, 0);
            if (job.ViewFront) views.Add((front, "正面図", "Front view"));
            if (job.ViewSide) views.Add((side, "側面図", "Side view"));
            if (job.View3D) views.Add((new View3(job.ViewAz, job.ViewEl), "立体図", "Isometric view"));
            if (views.Count == 0) views.Add((front, "正面図", "Front view"));

            double x = 0, sheetTop = scene.PoleTop, sheetBottom = scene.PoleBottom;
            bool first = true;
            foreach (var (v, cj, ce) in views)
            {
                var vd = new Drawing();
                Projector.Project(scene, v, new P2(0, 0), vd, job.Masks || job.CleanLines, 25, blockExists);
                if (job.CleanLines) HiddenLines.Clean(vd);
                vd.Bounds(out var x0, out var y0, out var x1, out var y1);
                double eqL = 0, eqR = 0;
                ItemExtents(scene, v, out eqL, out eqR);
                double leftNeed = 0;
                if (first && v == front)
                {
                    // room on the left for dimensions + labels
                    leftNeed = job.Dims ? 7 * th : 0;
                }
                double dx = first ? 0 : x - Math.Min(x0, eqL - leftNeed);
                var shift = new P2(dx, 0);
                foreach (var e in vd.E) Move(e, shift);
                d.E.AddRange(vd.E);
                double vx0 = x0 + dx, vx1 = x1 + dx;
                d.Views.Add(new ViewRegion { X0 = vx0, X1 = vx1, View = v, Offset = shift });
                if (job.GroundLine && Math.Abs(v.El) < 1e-9) Ground(d, job, vx0 - 400, vx1 + 400, th, first);
                if (v == front || (!job.ViewFront && first))
                {
                    double right = Labels(d, job, scene, v, shift, job.LabelH, eqL + dx, eqR + dx, bom, out double leftUsed, th);
                    vx1 = Math.Max(vx1, right); vx0 = Math.Min(vx0, leftUsed);
                    if (job.Dims) Dims(d, job, scene, eqL + dx, th);
                }
                double cy = Math.Min(y0, scene.PoleBottom) - 4 * th;
                var cap = d.Text(Lyr.Text, new P2((Math.Max(x0, eqL) + Math.Min(x1, eqR)) / 2 + dx, cy), th * 1.6, jp ? cj : ce, TAlign.Center);
                sheetBottom = Math.Min(sheetBottom, cy - 2 * th);
                sheetTop = Math.Max(sheetTop, y1);
                x = vx1 + 10 * th;
                first = false;
            }
            if (job.Bom && bom.Count > 0) Table(d, job, bom, new P2(x, sheetTop), th);
            return d;
        }

        /// <summary>One projected view (any direction) with hidden lines removed and duplicates merged, plus a caption.</summary>
        public static Drawing ViewOnly(PoleJob job, Scene scene, double az, double el, Func<string, bool> blockExists = null)
        {
            var d = new Drawing(); var v = new View3(az, el);
            Projector.Project(scene, v, new P2(0, 0), d, true, 25, blockExists);
            HiddenLines.Clean(d);
            d.Bounds(out var x0, out var y0, out var x1, out _);
            double th = job.TextPaper * job.Scale;
            bool horiz = Math.Abs(el) < 1e-6;
            if (horiz && job.GroundLine) Ground(d, job, x0 - 400, x1 + 400, th, true);
            double rel = ((az - job.ElevRot) % 360 + 540) % 360 - 180;
            string jp = !horiz ? "立体図" : Math.Abs(rel) < 1e-6 ? "正面図" : Math.Abs(rel + 90) < 1e-6 ? "側面図" : "立面図";
            string en = !horiz ? "Isometric view" : Math.Abs(rel) < 1e-6 ? "Front view" : Math.Abs(rel + 90) < 1e-6 ? "Side view" : "Elevation";
            if (horiz && Math.Abs(job.ElevRot) > 1e-6) { jp += $"（{job.ElevRot:0}°）"; en += $" ({job.ElevRot:0}°)"; }
            d.Text(Lyr.Text, new P2((x0 + x1) / 2, Math.Min(y0, scene.PoleBottom) - 3 * th), th * 1.6, job.LabelsBoth ? jp + " / " + en : job.LabelsJp ? jp : en, TAlign.Center);
            return d;
        }

        static void Move(DEnt e, P2 s)
        {
            switch (e)
            {
                case DLine l: for (int i = 0; i < l.Pts.Count; i++) l.Pts[i] += s; break;
                case DMask m: for (int i = 0; i < m.Pts.Count; i++) m.Pts[i] += s; break;
                case DText t: t.Pos += s; break;
                case DCircle c: c.C += s; break;
                case DDim dd: dd.A += s; dd.B += s; dd.At += dd.Vertical ? s.X : s.Y; break;
                case DTable tb: tb.TopLeft += s; break;
                case DInsert bi: bi.Pos += s; break;
                case DLeader ld: ld.Anchor += s; ld.Landing += s; ld.End += s; break;
            }
        }

        static void ItemExtents(Scene scene, View3 v, out double x0, out double x1)
        {
            x0 = double.MaxValue; x1 = double.MinValue;
            foreach (var it in scene.Items)
            {
                if (FarDefs.Contains(it.DefId)) continue;
                foreach (var p in it.Prims)
                {
                    IEnumerable<V3> pts = p is WireP w ? w.Pts : p.ToMesh()?.V;
                    if (pts == null) continue;
                    foreach (var q in pts) { if (q.Z < -100) continue; var s = v.P(q); x0 = Math.Min(x0, s.X); x1 = Math.Max(x1, s.X); }
                }
            }
            if (x0 > x1) { x0 = -500; x1 = 500; }
        }

        // ------------------------------------------------------------------ ground line

        static void Ground(Drawing d, PoleJob job, double xa, double xb, double th, bool withText)
        {
            d.E.Add(new DLine { Layer = Lyr.Ground, Pts = new List<P2> { new P2(xa, 0), new P2(xb, 0) } });
            if (job.Detail != Detail.Sketch)
                for (double xx = xa + 150; xx < xb; xx += 300)
                    d.E.Add(new DLine { Layer = Lyr.Ground, Pts = new List<P2> { new P2(xx, 0), new P2(xx - 120, -120) } });
            if (withText)
            {
                d.Text(Lyr.Text, new P2(xa + th * 3.2, th * 0.4), th, "GL", TAlign.Left);
                double s = th * 0.8, cx = xa + th * 2.2, cy = th * 0.15;   // ▽ symbol
                d.E.Add(new DLine { Layer = Lyr.Text, Closed = true, Pts = new List<P2> { new P2(cx - s / 2, cy + s), new P2(cx + s / 2, cy + s), new P2(cx, cy) } });
            }
        }

        // ------------------------------------------------------------------ labels

        public static double TextWidth(string s, double h)
        {
            double w = 0; foreach (var c in s ?? "") w += c > 0x2E7F ? h : c == ' ' ? h * 0.5 : h * 0.68;
            return w;
        }

        static string LabelText(SceneItem it, PoleJob job)
        {
            bool jp = job.LabelsJp; var n = job.NameOf(it);
            if (!string.IsNullOrWhiteSpace(it.Spec) && it.Spec.Length <= 28) n += jp ? "（" + it.Spec + "）" : " (" + it.Spec + ")";
            return n;
        }

        /// <param name="th">label text height</param><param name="dimTh">general text height (room for the dimension columns)</param>
        static double Labels(Drawing d, PoleJob job, Scene scene, View3 v, P2 shift, double th, double eqL, double eqR, List<BomRow> bom, out double leftUsed, double dimTh)
        {
            leftUsed = eqL;
            if (job.Labels == LabelMode.None) return eqR;
            bool jp = job.LabelsJp, balloons = job.Labels == LabelMode.Balloons;
            var rowOf = new Dictionary<string, int>();
            foreach (var r in bom) foreach (var id in r.ItemIds) rowOf[id] = r.No;
            var items = scene.Items.Where(i => i.Anchor.HasValue).ToList();
            var left = new List<(SceneItem it, P2 a)>(); var right = new List<(SceneItem it, P2 a)>();
            foreach (var it in items)
            {
                var a = v.P(it.Anchor.Value) + shift;
                bool toLeft = it.LabelSide < 0 || (it.LabelSide == 0 && a.X < shift.X);
                (toLeft ? left : right).Add((it, a));
            }
            double gap = (balloons ? 2.6 : 2.0) * th;
            double colR = eqR + 3 * th, colL = eqL - (job.Dims ? 6 * dimTh + 2 * th : 3 * th);
            double maxRight = colR;
            foreach (var (list, side) in new[] { (right, 1), (left, -1) })
            {
                list.Sort((p, q) => q.a.Y.CompareTo(p.a.Y));
                double prevY = double.MaxValue;
                foreach (var (it, a) in list)
                {
                    double y = Math.Min(a.Y + th * 0.6, prevY - gap); prevY = y;
                    double cx = side > 0 ? Math.Max(colR, a.X + th) : Math.Min(colL, a.X - th);
                    if (balloons) d.E.Add(new DCircle { Layer = Lyr.Text, C = a, R = th * 0.18, Filled = true });
                    if (balloons)
                    {
                        int no = rowOf.TryGetValue(it.ItemId, out var nn) ? nn : 0;
                        double r = th * 0.95; var c = new P2(cx + side * r, y);
                        d.E.Add(new DLine { Layer = Lyr.Text, Pts = new List<P2> { a, new P2(cx, y) } });
                        d.E.Add(new DCircle { Layer = Lyr.Text, C = c, R = r });
                        d.Text(Lyr.Text, new P2(c.X, c.Y - th * 0.45), th * 0.9, no.ToString(CultureInfo.InvariantCulture), TAlign.Center);
                        if (side > 0) maxRight = Math.Max(maxRight, c.X + r); else leftUsed = Math.Min(leftUsed, c.X - r);
                    }
                    else
                    {
                        string s = LabelText(it, job); double w = TextWidth(s, th) + th * 0.6;
                        double x2 = cx + side * w;
                        d.E.Add(new DLeader { Layer = Lyr.Text, Item = it.ItemId, Anchor = a, Landing = new P2(cx, y), End = new P2(x2, y), S = s, H = th, Side = side });
                        if (side > 0) maxRight = Math.Max(maxRight, x2); else leftUsed = Math.Min(leftUsed, x2);
                    }
                }
            }
            return maxRight;
        }

        // ------------------------------------------------------------------ dimensions

        static void Dims(Drawing d, PoleJob job, Scene scene, double eqL, double th)
        {
            double x1 = eqL - 2.5 * th, x2 = x1 - 3 * th;
            var lv = scene.Levels.Where(l => l.z >= -1).Select(l => Math.Round(l.z)).Distinct().OrderByDescending(z => z).ToList();
            var chain = new List<double>();
            foreach (var z in lv) if (chain.Count == 0 || chain[chain.Count - 1] - z >= 50) chain.Add(z);
            for (int i = 0; i + 1 < chain.Count; i++)
                d.E.Add(new DDim { Layer = Lyr.Dim, A = new P2(x1 + th, chain[i]), B = new P2(x1 + th, chain[i + 1]), Vertical = true, At = x1 });
            d.E.Add(new DDim { Layer = Lyr.Dim, A = new P2(x2 + th, scene.PoleTop), B = new P2(x2 + th, 0), Vertical = true, At = x2 });
            d.E.Add(new DDim { Layer = Lyr.Dim, A = new P2(x2 + th, 0), B = new P2(x2 + th, scene.PoleBottom), Vertical = true, At = x2 });
        }

        // ------------------------------------------------------------------ BOM

        public static List<BomRow> BuildBom(PoleJob job, Scene scene)
        {
            bool jp = job.LabelsJp;
            var rows = new List<BomRow>(); var map = new Dictionary<string, BomRow>(); var qty = new Dictionary<BomRow, double>();
            var order = scene.Items.Where(i => i.InBom && i.Qty > 0)
                .OrderBy(i => i.DefId == "pole" ? 0 : 1).ThenByDescending(i => i.Anchor?.Z ?? double.MinValue).ToList();
            foreach (var it in order)
            {
                string key = it.DefId + "|" + it.Spec + "|" + it.Name(true);
                if (!map.TryGetValue(key, out var r))
                {
                    var item = job.Items.FirstOrDefault(x => x.Id == it.ItemId);
                    var def = Catalog.Find(it.DefId);
                    r = new BomRow { No = rows.Count + 1, Name = job.NameOf(it), Spec = it.Spec ?? "", Remark = item?.Remark ?? def?.Remark ?? "" };
                    map[key] = r; rows.Add(r); qty[r] = 0;
                    r.Qty = def?.Unit ?? "本";
                }
                qty[r] += it.Qty; r.ItemIds.Add(it.ItemId);
            }
            foreach (var r in rows)
            {
                var unit = r.Qty; if (!jp) unit = EquipDef.UnitEn(unit);
                r.Qty = unit == "式" || unit == "set" ? (jp ? "1式" : "1 set") : qty[r].ToString("0.#", CultureInfo.InvariantCulture) + (jp ? unit : " " + unit);
            }
            return rows;
        }

        static void Table(Drawing d, PoleJob job, List<BomRow> bom, P2 tl, double th)
        {
            bool jp = job.LabelsJp; double s = job.Scale;
            var t = new DTable { Layer = Lyr.Bom, TopLeft = tl, RowH = th * 2.2, TextH = th, Title = jp ? "物品表" : "Bill of materials" };
            t.Rows.Add(jp ? new[] { "記号", "名称", "仕様・寸法", "数量", "備考" } : new[] { "No.", "Name", "Specification", "Qty", "Remarks" });
            foreach (var r in bom) t.Rows.Add(new[] { r.No.ToString(CultureInfo.InvariantCulture), r.Name, r.Spec, r.Qty, r.Remark });
            // column widths follow the longest text of each column (text height × character widths + margins)
            t.ColW = new double[5];
            for (int c = 0; c < 5; c++)
            {
                double w = t.Rows.Max(row => TextWidth(row[c], th));
                t.ColW[c] = Math.Max(w + th * 1.4, th * (c == 0 ? 3 : 4));
            }
            d.E.Add(t);
        }

        // ------------------------------------------------------------------ debug export (used by tests to render PNGs)

        public static string ToJson(Drawing d)
        {
            var sb = new StringBuilder("[");
            string P(P2 p) => string.Format(CultureInfo.InvariantCulture, "[{0:0.##},{1:0.##}]", p.X, p.Y);
            string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            bool first = true;
            foreach (var e in d.E)
            {
                if (!first) sb.Append(",\n"); first = false;
                switch (e)
                {
                    case DLine l: sb.Append($"{{\"t\":\"L\",\"l\":\"{l.Layer}\",\"k\":\"{l.Kind}\",\"c\":{(l.Closed ? 1 : 0)},\"p\":[{string.Join(",", l.Pts.Select(P))}]}}"); break;
                    case DLeader ld:
                        sb.Append($"{{\"t\":\"L\",\"l\":\"Text\",\"k\":\"Solid\",\"c\":0,\"p\":[{P(ld.Anchor)},{P(ld.Landing)},{P(ld.End)}]}},\n");
                        sb.Append($"{{\"t\":\"C\",\"p\":{P(ld.Anchor)},\"r\":{(ld.H * 0.18).ToString(CultureInfo.InvariantCulture)},\"f\":1}},\n");
                        sb.Append($"{{\"t\":\"T\",\"l\":\"Text\",\"p\":{P(new P2(ld.Landing.X + ld.Side * ld.H * 0.3, ld.Landing.Y + ld.H * 0.3))},\"h\":{ld.H.ToString(CultureInfo.InvariantCulture)},\"a\":\"{(ld.Side > 0 ? "Left" : "Right")}\",\"s\":{Q(ld.S)}}}");
                        break;
                    case DMask m: sb.Append($"{{\"t\":\"M\",\"p\":[{string.Join(",", m.Pts.Select(P))}]}}"); break;
                    case DText t: sb.Append($"{{\"t\":\"T\",\"l\":\"{t.Layer}\",\"p\":{P(t.Pos)},\"h\":{t.H.ToString(CultureInfo.InvariantCulture)},\"a\":\"{t.Align}\",\"s\":{Q(t.S)}}}"); break;
                    case DCircle c: sb.Append($"{{\"t\":\"C\",\"p\":{P(c.C)},\"r\":{c.R.ToString(CultureInfo.InvariantCulture)},\"f\":{(c.Filled ? 1 : 0)}}}"); break;
                    case DDim dd: sb.Append($"{{\"t\":\"D\",\"a\":{P(dd.A)},\"b\":{P(dd.B)},\"v\":{(dd.Vertical ? 1 : 0)},\"at\":{dd.At.ToString(CultureInfo.InvariantCulture)}}}"); break;
                    case DTable tb:
                        sb.Append($"{{\"t\":\"B\",\"p\":{P(tb.TopLeft)},\"rh\":{tb.RowH.ToString(CultureInfo.InvariantCulture)},\"th\":{tb.TextH.ToString(CultureInfo.InvariantCulture)},\"w\":[{string.Join(",", tb.ColW.Select(w => w.ToString(CultureInfo.InvariantCulture)))}],\"title\":{Q(tb.Title)},\"rows\":[{string.Join(",", tb.Rows.Select(r => "[" + string.Join(",", r.Select(Q)) + "]"))}]}}");
                        break;
                }
            }
            return sb.Append("]").ToString();
        }
    }
}
