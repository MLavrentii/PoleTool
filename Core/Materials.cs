using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PoleToolApp.Core
{
    /// <summary>
    /// Material take-off for the current pole: main equipment (BOM), fixing materials (bands, bolts, supports …),
    /// cable and pipe lengths – and an Excel workbook with all of it. Quantities of fixing materials follow
    /// common practice (pitches, counts per item) and are estimates to be checked.
    /// </summary>
    public static class Materials
    {
        public sealed class Row
        {
            public string Group, Name, Spec, Unit, Basis, For;
            public double Qty;
        }

        sealed class Txt { public string Jp, En; public Txt(string jp, string en) { Jp = jp; En = en; } }
        static string L(PoleJob j, string jp, string en) => j.LabelsBoth ? jp + " / " + en : j.LabelsJp ? jp : en;
        static string Tpl(string def) => Catalog.Find(def)?.Template;
        static double M(double mm) => mm / 1000.0;
        static double Up(double v, double step) => Math.Ceiling(v / step - 1e-9) * step;

        static double WireLen(SceneItem si) =>
            si.Prims.OfType<WireP>().Sum(w => { double s = 0; for (int k = 0; k + 1 < w.Pts.Count; k++) s += (w.Pts[k + 1] - w.Pts[k]).Len; return s; });

        static double OnPoleLen(SceneItem si, PoleSpec pole) =>
            si.Prims.OfType<WireP>().Sum(w =>
            {
                double s = 0;
                for (int k = 0; k + 1 < w.Pts.Count; k++)
                {
                    var a = w.Pts[k]; var b = w.Pts[k + 1];
                    bool on(V3 q) => q.Z > 0 && Math.Sqrt(q.X * q.X + q.Y * q.Y) < pole.RadiusAt(q.Z) + 80;
                    if (on(a) && on(b)) s += (b - a).Len;
                }
                return s;
            });

        /// <summary>Length of the straight pipe pieces of an item (frustums with the pipe radius), mm.</summary>
        static double PipeLen(SceneItem si, double dia, out double aboveGround)
        {
            double len = 0, above = 0;
            foreach (var f in si.Prims.OfType<FrustumP>())
            {
                if (Math.Abs(f.R1 - dia / 2) > 1 || Math.Abs(f.R2 - dia / 2) > 1) continue;
                double l = (f.B - f.A).Len; len += l;
                double z0 = Math.Min(f.A.Z, f.B.Z), z1 = Math.Max(f.A.Z, f.B.Z);
                if (z1 > 0 && z1 - z0 > 1) above += l * (Math.Min(z1, 1e9) - Math.Max(z0, 0)) / (z1 - z0);
            }
            aboveGround = above; return len;
        }

        static string PoleDia(PoleJob j, double z) => "φ" + (Math.Round(j.Pole.DiaAt(Math.Max(0, Math.Min(z, j.Pole.Top))) / 10) * 10).ToString("0", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ fixing materials

        public static List<Row> Fixings(PoleJob job, Scene scene)
        {
            var rows = new List<Row>();
            void Add(string gJp, string gEn, string nJp, string nEn, string spec, double qty, string uJp, string basisJp, string basisEn, SceneItem forItem)
            {
                if (qty <= 0) return;
                rows.Add(new Row
                {
                    Group = L(job, gJp, gEn), Name = L(job, nJp, nEn), Spec = spec, Qty = qty, Unit = job.LabelsJp ? uJp : EquipDef.UnitEn(uJp),
                    Basis = L(job, basisJp, basisEn), For = forItem == null ? "" : job.NameOf(forItem)
                });
            }
            double pitchPipe = job.Items.FirstOrDefault(i => i.On && i.Def == "pipe_band")?.Get("pitch", 1500) ?? 1500;
            foreach (var si in scene.Items)
            {
                var item = job.Item(si.ItemId); if (item == null) continue;
                string t = Tpl(si.DefId);
                double z = job.Z(item);
                switch (t)
                {
                    case "arm":
                        Add("腕金取付", "Crossarm fixing", "アームバンド", "Arm band", "柱径 " + PoleDia(job, z), 1, "個", "腕金1本につき1個", "1 per crossarm", si);
                        Add("腕金取付", "Crossarm fixing", "腕金取付ボルト・ナット・座金", "Arm bolts, nuts, washers", "M16", 2, "組", "腕金1本につき2組", "2 sets per crossarm", si);
                        break;
                    case "arm_tie":
                        Add("腕金取付", "Crossarm fixing", "アームタイ取付ボルト・ナット", "Arm-tie bolts and nuts", "M12", 2 * Math.Max(1, si.Qty), "組", "アームタイ1本につき2組", "2 sets per arm tie", si);
                        break;
                    case "pas":
                        Add("機器取付", "Equipment fixing", "装柱バンド", "Pole band", "柱径 " + PoleDia(job, z), 2, "個", "開閉器1台につき2個", "2 per switch", si);
                        Add("機器取付", "Equipment fixing", "取付ボルト・ナット", "Bolts and nuts", "M16", 4, "組", "開閉器1台につき4組", "4 sets per switch", si);
                        break;
                    case "vct":
                        Add("機器取付", "Equipment fixing", "VCT取付ボルト・ナット", "VCT fixing bolts", "M12", 4, "組", "1台につき4組", "4 sets per unit", si);
                        break;
                    case "box": case "cyl":
                        Add("機器取付", "Equipment fixing", "箱取付バンド", "Box mounting band", "柱径 " + PoleDia(job, z), 2, "個", "1面につき2個", "2 per box", si);
                        Add("機器取付", "Equipment fixing", "取付ボルト・ナット", "Bolts and nuts", "M10", 4, "組", "1面につき4組", "4 sets per box", si);
                        break;
                    case "pin_ins":
                        Add("碍子取付", "Insulator fixing", "ピン碍子用ボルト・ナット", "Pin-insulator bolts", "M16", Math.Max(1, si.Qty), "組", "碍子1個につき1組", "1 set per insulator", si);
                        break;
                    case "cable_head":
                        Add("ケーブル支持", "Cable support", "ケーブル支持金具（端末用）", "Termination support bracket", "", Math.Max(1, si.Qty), "個", "端末1箇所につき1個", "1 per termination", si);
                        break;
                    case "guy":
                        Add("支線", "Guy", "支線バンド", "Guy band", "柱径 " + PoleDia(job, z), 1, "個", "支線1条につき1個", "1 per guy", si);
                        Add("支線", "Guy", "シンブル", "Thimble", "", 2, "個", "上下各1", "top + bottom", si);
                        break;
                    case "riser_pipe": case "conduit":
                        {
                            double dia = item.Get("dia", t == "riser_pipe" ? 113.4 : 34);
                            PipeLen(si, dia, out var above);
                            int bands = (int)Math.Floor(Math.Max(0, above - 400) / pitchPipe) + 2;
                            Add("管の固定", "Pipe fixing", "自在バンド（管固定用）", "Pipe band", $"{item.Spec} 用 / 柱径 {PoleDia(job, above / 2)}", bands, "個",
                                $"地上 {M(above):0.0} m ÷ ピッチ {M(pitchPipe):0.0} m + 端部", $"{M(above):0.0} m above GL ÷ {M(pitchPipe):0.0} m pitch + ends", si);
                            Add("管の固定", "Pipe fixing", "ノーマルベンド", "Normal bend", item.Spec, 1, "個", "地際の立上り", "at ground level", si);
                            if (t == "riser_pipe") Add("管の固定", "Pipe fixing", "エントランスキャップ", "Entrance cap", item.Spec, 1, "個", "管上端", "pipe top", si);
                            break;
                        }
                    case "ctrl_cable": case "meter_cable": case "earth": case "link_cable":
                        {
                            double on = OnPoleLen(si, job.Pole);
                            int ties = on > 0 ? (int)Math.Ceiling(on / 600) + 1 : 0;
                            Add("ケーブル支持", "Cable support", "ケーブル結束バンド（耐候性）", "Cable ties (weather-proof)", "", ties, "本", $"柱沿い {M(on):0.0} m ÷ 0.6 m + 1", $"{M(on):0.0} m on the pole ÷ 0.6 m + 1", si);
                            if (t == "earth")
                            {
                                Add("接地", "Earthing", "接地線保護管（合成樹脂管）", "Earth-wire protection pipe", "VE16 L=2.5m", 1, "本", "地表上2.5mまで保護（電技解釈第17条）", "protected up to 2.5 m above GL", si);
                                Add("接地", "Earthing", "圧着端子", "Crimp lugs", item.Spec, 2, "個", "両端", "both ends", si);
                            }
                            break;
                        }
                    case "earth_rod":
                        Add("接地", "Earthing", "接地極用リード端子", "Electrode clamp", "", Math.Max(1, si.Qty), "個", "接地極1本につき1個", "1 per electrode", si);
                        break;
                }
            }
            return rows;
        }

        // ------------------------------------------------------------------ cables and pipes (lengths)

        public static List<Row> Lengths(PoleJob job, Scene scene)
        {
            var rows = new List<Row>();
            foreach (var si in scene.Items)
            {
                var item = job.Item(si.ItemId); if (item == null) continue;
                string t = Tpl(si.DefId);
                switch (t)
                {
                    case "jumper": case "hv_cable": case "ctrl_cable": case "meter_cable": case "earth": case "guy": case "link_cable":
                        {
                            double len = WireLen(si); if (len <= 0) break;
                            double withSlack = Up(M(len) * 1.1 + (t == "guy" ? 1.0 : 2.0), 1);
                            rows.Add(new Row
                            {
                                Group = L(job, "ケーブル・電線", "Cables and wires"), Name = job.NameOf(si), Spec = item.Spec, Qty = withSlack, Unit = "m",
                                Basis = L(job, $"図上 {M(len):0.0} m × 1.1 + 端末余長", $"drawn {M(len):0.0} m × 1.1 + ends")
                            });
                            break;
                        }
                    case "riser_pipe": case "conduit": case "ug_pipe":
                        {
                            double dia = item.Get("dia", 50);
                            double len = PipeLen(si, dia, out _);
                            if (t == "ug_pipe") len = si.Prims.OfType<FrustumP>().Sum(f => (f.B - f.A).Len);
                            string spec = item.Spec ?? "";
                            double std = spec.StartsWith("G", StringComparison.OrdinalIgnoreCase) ? 3.66 : spec.StartsWith("VE", StringComparison.OrdinalIgnoreCase) ? 4.0 : 0;
                            double m = Up(M(len), 0.1);
                            rows.Add(new Row
                            {
                                Group = L(job, "電線管", "Conduits"), Name = job.NameOf(si), Spec = spec, Qty = m, Unit = "m",
                                Basis = std > 0 ? L(job, $"定尺 {std:0.##} m × {Math.Ceiling(m / std):0} 本（カップリング {Math.Max(0, Math.Ceiling(m / std) - 1):0} 個）",
                                                         $"{Math.Ceiling(m / std):0} lengths of {std:0.##} m ({Math.Max(0, Math.Ceiling(m / std) - 1):0} couplings)")
                                                : L(job, "長さで手配", "ordered by length")
                            });
                            break;
                        }
                }
            }
            return rows;
        }

        // ------------------------------------------------------------------ workbook

        public static void WriteExcel(PoleJob job, Scene scene, string path)
        {
            bool jp = job.LabelsJp;
            var bom = Sheet.BuildBom(job, scene);
            var fix = Fixings(job, scene);
            var len = Lengths(job, scene);
            var x = new Xlsx();
            string date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string title = string.IsNullOrWhiteSpace(job.Title) ? (string.IsNullOrWhiteSpace(job.ConfigName) ? job.Pole.Designation(jp) : job.ConfigName) : job.Title;

            // --- summary
            var s0 = x.Add(L(job, "概要", "Summary")); s0.Widths = new[] { 26.0, 44 };
            s0.Set(0, 0, L(job, "装柱 材料表", "Pole material list"), Xlsx.St.Title);
            s0.Set(1, 0, title, Xlsx.St.Sub);
            int r = 3;
            void Kv(string k, object v) { s0.Set(r, 0, k, Xlsx.St.Group); s0.Set(r, 1, v, v is double ? Xlsx.St.Num : Xlsx.St.Cell); r++; }
            Kv(L(job, "作成日", "Date"), date);
            Kv(L(job, "柱", "Pole"), job.Pole.Designation(jp));
            Kv(L(job, "全長 (m)", "Length (m)"), job.Pole.Length / 1000);
            Kv(L(job, "根入れ (m)", "Embedment (m)"), job.Pole.EmbedUsed / 1000);
            Kv(L(job, "地上高 (m)", "Height above GL (m)"), job.Pole.Top / 1000);
            Kv(L(job, "詳細度", "Detail"), job.Detail.ToString());
            Kv(L(job, "機器（行数）", "Equipment lines"), (double)bom.Count);
            Kv(L(job, "取付材料（行数）", "Fixing material lines"), (double)fix.Count);
            Kv(L(job, "ケーブル・管（行数）", "Cable / pipe lines"), (double)len.Count);
            r++;
            s0.Set(r, 0, L(job, "取付材料の数量は一般的なピッチ・個数による概算です。メーカー資料・施工条件で確認してください。",
                                "Fixing-material quantities are estimates from usual pitches / counts. Check against maker data and site conditions."), Xlsx.St.Note);
            s0.Merge(r, 0, r, 1);

            // --- BOM
            var s1 = x.Add(L(job, "物品表", "BOM")); s1.Widths = new[] { 6.0, 40, 36, 9, 7, 24 }; s1.FreezeRow = 3; s1.Landscape = true;
            s1.Set(0, 0, L(job, "物品表（主要機器）", "Bill of materials (equipment)"), Xlsx.St.Title);
            string[] h1 = jp ? new[] { "記号", "名称", "仕様・寸法", "数量", "単位", "備考" } : new[] { "No.", "Name", "Specification", "Qty", "Unit", "Remarks" };
            for (int c = 0; c < h1.Length; c++) s1.Set(2, c, h1[c], Xlsx.St.Header);
            r = 3;
            foreach (var b in bom)
            {
                var (q, u) = SplitQty(b.Qty);
                s1.Set(r, 0, (double)b.No, Xlsx.St.CellCenter); s1.Set(r, 1, b.Name); s1.Set(r, 2, b.Spec);
                if (q.HasValue) s1.Set(r, 3, q.Value, Xlsx.St.Num); else s1.Set(r, 3, b.Qty, Xlsx.St.Cell);
                s1.Set(r, 4, u, Xlsx.St.CellCenter); s1.Set(r, 5, b.Remark); r++;
            }

            // --- fixing materials grouped
            var s2 = x.Add(L(job, "取付材料", "Fixing materials")); s2.Widths = new[] { 6.0, 32, 26, 9, 7, 40, 30 }; s2.FreezeRow = 3; s2.Landscape = true;
            s2.Set(0, 0, L(job, "取付材料・副資材", "Fixing and auxiliary materials"), Xlsx.St.Title);
            string[] h2 = jp ? new[] { "No.", "品名", "仕様・寸法", "数量", "単位", "算出根拠", "対象" } : new[] { "No.", "Item", "Specification", "Qty", "Unit", "Basis", "For" };
            for (int c = 0; c < h2.Length; c++) s2.Set(2, c, h2[c], Xlsx.St.Header);
            r = 3; int no = 1;
            foreach (var g in fix.GroupBy(f => f.Group))
            {
                s2.Set(r, 0, g.Key, Xlsx.St.Group); for (int c = 1; c < 7; c++) s2.Set(r, c, null, Xlsx.St.Group); s2.Merge(r, 0, r, 6); r++;
                foreach (var f in g)
                {
                    s2.Set(r, 0, (double)no++, Xlsx.St.CellCenter); s2.Set(r, 1, f.Name); s2.Set(r, 2, f.Spec); s2.Set(r, 3, f.Qty, Xlsx.St.Num);
                    s2.Set(r, 4, f.Unit, Xlsx.St.CellCenter); s2.Set(r, 5, f.Basis); s2.Set(r, 6, f.For); r++;
                }
            }
            // totals per item + spec
            r++;
            s2.Set(r, 0, L(job, "集計（品名・仕様ごと）", "Totals (per item and specification)"), Xlsx.St.Sub); r++;
            for (int c = 0; c < 5; c++) s2.Set(r, c, (jp ? new[] { "No.", "品名", "仕様・寸法", "合計", "単位" } : new[] { "No.", "Item", "Specification", "Total", "Unit" })[c], Xlsx.St.Header);
            r++; no = 1;
            foreach (var g in fix.GroupBy(f => (f.Name, f.Spec, f.Unit)).OrderBy(g => g.Key.Name))
            {
                s2.Set(r, 0, (double)no++, Xlsx.St.CellCenter); s2.Set(r, 1, g.Key.Name); s2.Set(r, 2, g.Key.Spec);
                s2.Set(r, 3, g.Sum(f => f.Qty), Xlsx.St.NumBold); s2.Set(r, 4, g.Key.Unit, Xlsx.St.CellCenter); r++;
            }

            // --- cables and pipes
            var s3 = x.Add(L(job, "ケーブル・管", "Cables & pipes")); s3.Widths = new[] { 6.0, 18, 36, 26, 10, 7, 46 }; s3.FreezeRow = 3; s3.Landscape = true;
            s3.Set(0, 0, L(job, "ケーブル・電線管 数量", "Cable and conduit lengths"), Xlsx.St.Title);
            string[] h3 = jp ? new[] { "No.", "区分", "名称", "仕様", "数量", "単位", "算出根拠" } : new[] { "No.", "Group", "Name", "Specification", "Qty", "Unit", "Basis" };
            for (int c = 0; c < h3.Length; c++) s3.Set(2, c, h3[c], Xlsx.St.Header);
            r = 3; no = 1;
            foreach (var l in len.OrderBy(l => l.Group))
            {
                s3.Set(r, 0, (double)no++, Xlsx.St.CellCenter); s3.Set(r, 1, l.Group); s3.Set(r, 2, l.Name); s3.Set(r, 3, l.Spec);
                s3.Set(r, 4, l.Qty, Xlsx.St.Num); s3.Set(r, 5, l.Unit, Xlsx.St.CellCenter); s3.Set(r, 6, l.Basis); r++;
            }
            x.Save(path);
        }

        /// <summary>"6個" / "6 pcs" / "1式" -> (6, "個") ; non-numeric -> (null, text).</summary>
        static (double? q, string unit) SplitQty(string s)
        {
            s = (s ?? "").Trim(); int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i == 0) return (null, s);
            double.TryParse(s.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out var q);
            return (q, s.Substring(i).Trim());
        }
    }
}
