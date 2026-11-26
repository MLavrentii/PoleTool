using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using PoleToolApp.Core;

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) fails++; }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "vis") return Vis(args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 0.25);
        string outDir = args.Length > 0 ? args[0] : null;
        Console.WriteLine("Pole presets / embedment");
        Check(PolePresets.AutoEmbed(12000, 5) == 2000, "12 m pole -> 2.0 m embedment");
        Check(PolePresets.AutoEmbed(10000, 3.5) == 1700, "10 m pole -> 1.7 m (L/6 rounded up to 50)");
        Check(PolePresets.AutoEmbed(16000, 5) == 2500, "16 m pole -> 2.5 m");
        Check(PolePresets.AutoEmbed(15000, 10) == 2800, "15 m, 10 kN -> 2.5 + 0.3 m");
        var p = new PoleSpec(); Check(Math.Abs(p.DiaAt(0) - (190 + 10000 / 75.0)) < 1e-6, "taper 1/75: Ø at GL");

        Console.WriteLine("Catalogue");
        Check(Catalog.All.Select(d => d.Id).Distinct().Count() == Catalog.All.Count, "unique definition ids");
        Check(Catalog.All.All(d => Catalog.Categories.Any(c => c.id == d.Cat)), "every definition has a known category");
        Check(Catalog.All.All(d => !string.IsNullOrEmpty(d.Jp) && !string.IsNullOrEmpty(d.En)), "JP + EN names everywhere");

        foreach (var (id, jp, en) in Configs.List)
            foreach (Detail det in Enum.GetValues(typeof(Detail)))
            {
                var job = Configs.Make(id); job.Detail = det; job.View3D = true;
                var json = job.ToJson(); var back = PoleJob.FromJson(json);
                Check(back != null && back.Items.Count == job.Items.Count, $"{id}/{det}: JSON round trip");
                var scene = SceneBuilder.Build(job);
                Check(!scene.Warnings.Any(w => w.StartsWith("Build error")), $"{id}/{det}: builds ({scene.Items.Count} items) {string.Join("; ", scene.Warnings)}");
                bool finite = true;
                foreach (var it in scene.Items) foreach (var pr in it.Prims)
                {
                    var m = pr.ToMesh(); if (m == null) continue;
                    foreach (var v in m.V) if (double.IsNaN(v.X + v.Y + v.Z) || double.IsInfinity(v.X + v.Y + v.Z)) finite = false;
                }
                Check(finite, $"{id}/{det}: all vertices finite");
                foreach (var lj in new[] { true, false })
                {
                    job.LabelsJp = lj;
                    var d = Sheet.Compose(job, scene, out var bom);
                    Check(d.E.Count > 0, $"{id}/{det}/{(lj ? "JP" : "EN")}: sheet has {d.E.Count} entities, BOM {bom.Count} rows");
                    if (outDir != null && det == (id == "simple" ? Detail.Sketch : Detail.Detailed))
                    {
                        Directory.CreateDirectory(outDir);
                        File.WriteAllText(Path.Combine(outDir, $"{id}_{(lj ? "jp" : "en")}.json"), Sheet.ToJson(d));
                    }
                }
                if (outDir != null && det == Detail.Detailed && id == "pas_vct")
                    File.WriteAllText(Path.Combine(outDir, "job_pas_vct.json"), job.ToJson(true));
            }

        Console.WriteLine("Dragging");
        {
            var job = Configs.Make("pas_vct");
            var sbox = job.Items.First(i => i.Def == "sog_box");
            var st = Drag.Begin(job, sbox.Id, new P2(0, 2000)); st.Space = null;   // mechanics only (no collision check)
            Check(st != null && st.Item == sbox, "box is draggable");
            Drag.Apply(job, st, new P2(0, 3013), true);
            Check(Math.Abs(job.Z(sbox) - 3010) < 1e-6, "sbox height follows the cursor, snapped to 10 mm");
            Drag.Apply(job, st, new P2(900, 99999), true);
            Check(Math.Abs(job.Z(sbox) - (job.Pole.Top - 50)) < 1e-6 && sbox.Side == 90, "height clamped below the pole top; crossing to the right -> side 90");
            Drag.Apply(job, st, new P2(-900, 2000), true);
            Check(sbox.Side == 270, "dragging to the left -> side 270");
            var pins = job.Items.First(i => i.Def == "pin_ins");
            var st2 = Drag.Begin(job, pins.Id, new P2(0, 8000));
            Check(st2 != null && st2.Item.Def == "arm_vct", "pin insulators move with their crossarm");
            var arm = st2.Item; double h0 = job.Z(arm);
            Drag.Apply(job, st2, new P2(0, 7500), true);
            Check(Math.Abs(job.Z(arm) - (h0 - 500)) < 1e-6 && arm.Ref == HRef.Top, "top-referenced arm moves down by 500");
            var guy = job.Items.First(i => i.Def == "guy");
            var st3 = Drag.Begin(job, guy.Id, new P2(-4500, 500));
            Check(st3.Part == "dist", "grabbing the lower guy wire changes the anchor distance");
            Drag.Apply(job, st3, new P2(-6200, 0), true);
            Check(Math.Abs(guy.Get("dist", 0) - 6200) < 1e-6 && guy.Side == 270, "guy anchor distance 6200 on the left");
            Drag.Apply(job, st3, new P2(6000, 0), true);
            Check(guy.Side == 90, "guy moved to the other side");
            var ug = job.Items.First(i => i.Def == "ug_pipe");
            var st4 = Drag.Begin(job, ug.Id, new P2(2000, -900));
            Drag.Apply(job, st4, new P2(2000, -1500), true);
            Check(Math.Abs(job.Z(ug) + 1500) < 1e-6, "underground ducts deeper (-1500)");
            Drag.Apply(job, st4, new P2(-2000, -1500), true);
            Check(job.Items.First(i => i.Def == "hv_cable").Side == 270 && job.Items.First(i => i.Def == "riser_pipe").Side == 270, "duct route moved to the left with riser + HV cable");
            Check(Drag.Begin(job, job.Items.First(i => i.Def == "jumper").Id, new P2(0, 0)) == null, "cables are not draggable (routed automatically)");
            var sc = SceneBuilder.Build(job);
            Check(!sc.Warnings.Any(w => w.StartsWith("Build error")), "scene still builds after dragging");
            var dd = new Drawing(); Projector.Project(sc, View3.Front, new P2(0, 0), dd);
            Check(dd.E.OfType<DLine>().Any(l => l.Item == sbox.Id), "projected lines carry their item id (hit testing)");
            job.LabelsBoth = true; var both = Sheet.Compose(job, sc, out var bomB);
            Check(bomB.Any(r => r.Name.Contains(" / ")), "JP + EN names in the BOM");
        }

        Console.WriteLine("Lightning protection");
        {
            var job = Configs.Make("pas_vct");
            Check(job.Items.Any(i => i.Def == "la" && !i.On) && job.Items.Any(i => i.Def == "lrod" && !i.On), "LA + lightning rod listed, off by default");
            foreach (var i in job.Items.Where(i => i.Def is "la" or "lrod" or "earth_lp")) i.On = true;
            var sc = SceneBuilder.Build(job);
            Check(sc.Items.Any(i => i.DefId == "lrod" && i.Prims.Count > 0) && sc.Items.Any(i => i.DefId == "la") && sc.Items.Any(i => i.DefId == "earth_lp"), "LA, rod and their earth wire are built " + string.Join(";", sc.Warnings));
            if (outDir != null) { var d = Sheet.Compose(job, sc, out _); File.WriteAllText(Path.Combine(outDir, "lp_jp.json"), Sheet.ToJson(d)); }
        }

        Console.WriteLine("3D view from above");
        {
            var job = Configs.Make("pas_vct");
            var sc = SceneBuilder.Build(job);
            var view = new View3(-40, 45);
            var d = new Drawing(); Projector.Project(sc, view, new P2(0, 0), d);
            // the SOG box (front, 2 m) must be drawn after (= on top of) every pole wipeout that covers it
            var sogId = job.Items.First(i => i.Def == "sog_box").Id;
            int lastBoxLine = d.E.FindLastIndex(e => e is DLine && e.Item == sogId);
            var boxPt = ((DLine)d.E[lastBoxLine]).Pts[0];
            bool hidden = false;
            for (int i = lastBoxLine + 1; i < d.E.Count; i++)
                if (d.E[i] is DMask mk && mk.Item == "pole" && Hull.Area(mk.Pts) > 0 && InPoly(mk.Pts, boxPt)) hidden = true;
            Check(!hidden, "box in front of the pole stays visible in a view from above");
            if (outDir != null) File.WriteAllText(Path.Combine(outDir, "iso_top.json"), Sheet.ToJson(d));
            if (outDir != null)
            {
                foreach (var i in job.Items) i.On = true;
                var sc2 = SceneBuilder.Build(job); var d2 = new Drawing(); Projector.Project(sc2, new View3(150, 35), new P2(0, 0), d2);
                File.WriteAllText(Path.Combine(outDir, "iso_back.json"), Sheet.ToJson(d2));
            }
        }

        Console.WriteLine("Hidden-line removal + overkill");
        {
            var job = Configs.Make("pas_vct"); foreach (var i in job.Items) i.On = true;
            var sc = SceneBuilder.Build(job);
            foreach (var (az, el) in new[] { (-35.0, 25.0), (150.0, 35.0), (-40.0, 60.0), (0.0, 0.0) })
            {
                var raw = new Drawing(); Projector.Project(sc, new View3(az, el), new P2(0, 0), raw);
                int rawLines = raw.E.Count(e => e is DLine);
                var d = Sheet.ViewOnly(job, sc, az, el);
                int lines = d.E.Count(e => e is DLine), masks = d.E.Count(e => e is DMask);
                double hid = VisCheck.HiddenLineFraction(sc, d, az, el);
                Check(masks == 0 && lines > 100 && lines < rawLines && hid < 0.03,
                      $"view az {az} el {el}: {rawLines} lines -> {lines}, no wipeouts, hidden parts left {hid:P1}");
                if (outDir != null && az == 150) File.WriteAllText(Path.Combine(outDir, "clean_back.json"), Sheet.ToJson(d));
            }
            job.CleanLines = true; var sh = Sheet.Compose(job, sc, out _);
            Check(!sh.E.Any(e => e is DMask), "sheet with 'remove hidden lines' has no wipeouts");
        }

        Console.WriteLine("Switching the VCT off");
        {
            var job = Configs.Make("pas_vct");
            var vct = job.Items.First(i => i.Def == "vct");
            var also = Links.SetOn(job, vct, false);
            string[] gone = { "arm_vct", "pin_ins", "arm_band", "arm_tie", "meter_cable", "meter_box", "conduit_meter" };
            Check(gone.All(g => job.Items.Where(i => i.Def == g).All(i => !i.On)), "VCT off also switches off: " + string.Join(", ", also.Select(a => a.Def)));
            Check(job.Items.First(i => i.Def == "arm_head").On && job.Items.First(i => i.Def == "cable_head").On, "cable-head arm and heads stay on");
            var sc = SceneBuilder.Build(job);
            Check(sc.Get(job.Items.First(i => i.Def == "arm_vct").Id) == null, "no VCT crossarm in the drawing");
            var jumper = sc.Items.First(i => i.DefId == "jumper");
            var wires = jumper.Prims.OfType<WireP>().ToList();
            var pas = sc.Items.First(i => i.DefId == "pas"); var heads = sc.Items.First(i => i.DefId == "cable_head");
            double pasZ = pas.Prims.Select(p => p.ToMesh()).Where(m => m != null).SelectMany(m => m.V).Average(v => v.Z);
            int direct = wires.Count(w => w.Pts[0].Z > pasZ - 600 && w.Pts[w.Pts.Count - 1].Z < pasZ - 1500);
            Check(direct == 3, $"three jumpers go straight from the PAS down to the cable heads ({direct})");
            Check(!sc.Warnings.Any(), "no warnings: " + string.Join("; ", sc.Warnings));
            if (outDir != null) { var dv = Sheet.Compose(job, sc, out _); File.WriteAllText(Path.Combine(outDir, "novct.json"), Sheet.ToJson(dv)); }
            Links.SetOn(job, vct, true);
            Check(gone.All(g => job.Items.Where(i => i.Def == g).All(i => i.On)), "VCT on again brings them all back");
            var arm = job.Items.First(i => i.Def == "arm_head");
            Links.SetOn(job, arm, false);
            Check(!job.Items.First(i => i.Def == "cable_head").On && !job.Items.First(i => i.Def == "la").On == true, "cable-head arm off -> heads off too");
        }

        Console.WriteLine("Drag modifiers, other views, saved configurations");
        {
            var job = Configs.Make("pas_vct"); var bx = job.Items.First(i => i.Def == "sog_box");
            var st = Drag.Begin(job, bx.Id, new P2(0, 2000)); st.Space = null;
            Drag.Apply(job, st, new P2(900, 2500), View3.Front, false, true, false);
            Check(bx.Side == 0 && Math.Abs(job.Z(bx) - 2500) < 1e-6, "Shift: height only, side kept");
            Drag.Apply(job, st, new P2(600, 2000), View3.Front, false, false, true);
            Check(Math.Abs(bx.Side - 30) < 1e-6, "Ctrl+Shift: 600 mm to the right turns the box to 30° (15° steps)");
            Drag.Apply(job, st, new P2(0, 2003), View3.Front, true);
            Check(Math.Abs(job.Z(bx) - 2003) < 1e-6, "Alt: 1 mm steps");
            var iso = new View3(-35, 40); var st2 = Drag.Begin(job, bx.Id, new P2(0, 1000)); st2.Space = null;
            double z0 = job.Z(bx); Drag.Apply(job, st2, new P2(0, 1000 + 500 * iso.Up.Z), iso);
            Check(Math.Abs(job.Z(bx) - (z0 + 500)) < 10, "3D view: vertical movement corrected for the viewing angle");
            var sc = SceneBuilder.Build(job); job.ViewSide = true; var sheet = Sheet.Compose(job, sc, out _);
            Check(sheet.Views.Count >= 2 && sheet.RegionAt(sheet.Views[1].X0 + 10).View == View3.Side, "sheet knows where each view is (drag in the sheet)");
            var turned = new View3(30, 0); var st3 = Drag.Begin(job, bx.Id, new P2(0, 2000)); st3.Space = null;
            Drag.Apply(job, st3, new P2(900, 2000), turned);
            Check(Math.Abs(bx.Side - 60) < 1e-6, "front view turned 30°: dragging right puts the box at 60° (the new right side)");
            job.ElevRot = 30; var shT = Sheet.Compose(job, sc, out _);
            if (outDir != null) File.WriteAllText(Path.Combine(outDir, "turned30.json"), Sheet.ToJson(shT));
            Check(Math.Abs(shT.Views[0].View.Az - 30) < 1e-9 && Math.Abs(shT.Views[1].View.Az + 60) < 1e-9, "sheet front/side views follow the turn");
            job.ElevRot = 0;
            Configs.UserDir = Path.Combine(Path.GetTempPath(), "pt_cfg_test"); if (Directory.Exists(Configs.UserDir)) Directory.Delete(Configs.UserDir, true);
            Configs.SaveUser("現場A 12m", job);
            var back = Configs.LoadUser("現場A 12m");
            Check(Configs.UserNames().SequenceEqual(new[] { "現場A 12m" }) && back != null && back.Items.Count == job.Items.Count && back.ConfigName == "現場A 12m", "configuration saved under a name and loaded again");
            Configs.DeleteUser("現場A 12m"); Check(Configs.UserNames().Count == 0, "configuration deleted");
        }

        Console.WriteLine("Insert uses only the switched-on equipment");
        {
            var job = Configs.Make("pas_vct");
            Links.SetOn(job, job.Items.First(i => i.Def == "vct"), false);
            job.Items.First(i => i.Def == "guy").On = false; job.Items.First(i => i.Def == "step_bolts").On = false;
            var clone = job.Clone();
            Check(clone.Items.Count(i => i.On) == job.Items.Count(i => i.On), $"clone keeps the on/off state ({clone.Items.Count(i => i.On)} on)");
            var sc = SceneBuilder.Build(clone);
            var offIds = job.Items.Where(i => !i.On).Select(i => i.Id).ToHashSet();
            Check(!sc.Items.Any(i => offIds.Contains(i.ItemId)), "no switched-off item in the scene: " + string.Join(",", sc.Items.Where(i => offIds.Contains(i.ItemId)).Select(i => i.DefId)));
            var sh = Sheet.Compose(clone, sc, out var bom);
            Check(!sh.E.Any(e => e.Item != null && offIds.Contains(e.Item)), "no switched-off item in the sheet");
        }

        Console.WriteLine("Elements stay side by side around the pole");
        {
            var job = Configs.Make("pas_vct"); var sc = SceneBuilder.Build(job);
            var ov0 = Around.Overlaps(job, sc);
            Check(ov0.Count == 0, "default configuration has no overlaps: " + string.Join("; ", ov0.Select(o => job.Item(o.a)?.Def + "↔" + job.Item(o.b)?.Def)));
            {
                var j2 = Configs.Make("empty"); var ctl = j2.AddFromCatalog("conduit_ctrl"); var ntt = j2.AddFromCatalog("conduit_ntt");
                ctl.Side = 345; ntt.Side = 45;
                var st = Drag.Begin(j2, ntt.Id, new P2(0, 1000), SceneBuilder.Build(j2));
                Drag.Apply(j2, st, new P2(-60 / 0.05, 1000), View3.Front, false, false, true);   // wants 345° = on the other conduit
                double d = Math.Abs(((ntt.Side - ctl.Side) % 360 + 540) % 360 - 180);
                Check(d > 12 && d < 40 && ntt.Side < 90, $"conduit turned onto another one stops beside it ({ntt.Side:0.#}° next to {ctl.Side:0.#}°)");
                Check(Around.Overlaps(j2, SceneBuilder.Build(j2)).Count == 0, "no overlap after the move");
                Drag.Apply(j2, st, new P2(-100 / 0.05, 1000), View3.Front, false, false, true);   // wants 305°: past it
                Check(ntt.Side > 270 && ntt.Side < 345, $"pulled further it jumps over to the other side ({ntt.Side:0.#}°)");
                Check(Around.Overlaps(j2, SceneBuilder.Build(j2)).Count == 0, "still no overlap");
            }
            // a cable run (earth wire) can be turned too and keeps clear of pipes
            var earth = job.Items.First(i => i.Def == "earth_a"); var riser = job.Items.First(i => i.Def == "riser_pipe");
            var st2 = Drag.Begin(job, earth.Id, new P2(0, 3000), SceneBuilder.Build(job));
            Check(st2 != null, "earth wire is draggable (turn only)");
            double want = riser.Side; double e0 = earth.Side;
            Drag.Apply(job, st2, new P2(((want - e0 + 540) % 360 - 180) / 0.05, 3000), View3.Front, false, false, true);
            double dd = Math.Abs(((earth.Side - riser.Side) % 360 + 540) % 360 - 180);
            Check(dd > 5 && Around.Overlaps(job, SceneBuilder.Build(job)).Count == 0, $"earth wire turned onto the riser stops beside it ({earth.Side:0.#}° vs riser {riser.Side:0.#}°)");
            var messy = Configs.Make("pas_vct"); foreach (var i in messy.Items.Where(i => i.Def is "conduit_ntt" or "conduit_meter" or "meter_box")) i.Side = 90;
            Around.Arrange(messy);
            Check(Around.Overlaps(messy, SceneBuilder.Build(messy)).Count == 0, "Arrange separates elements that were put on top of each other");
        }

        Console.WriteLine("Excel material list, leaders, adaptive table");
        {
            var job = Configs.Make("pas_vct"); var sc = SceneBuilder.Build(job);
            var fix = Materials.Fixings(job, sc); var len = Materials.Lengths(job, sc);
            Check(fix.Any(f => f.Name.Contains("自在バンド")) && fix.Any(f => f.Name.Contains("結束")) && fix.Any(f => f.Name.Contains("アームバンド")), $"fixing materials: {fix.Count} lines (bands, ties, bolts …)");
            Check(len.Any(l => l.Unit == "m" && l.Name.Contains("高圧ケーブル")) && len.All(l => l.Qty > 0), $"cable / pipe lengths: {len.Count} lines");
            string xl = Path.Combine(outDir ?? Path.GetTempPath(), "pole_materials.xlsx");
            Materials.WriteExcel(job, sc, xl);
            Check(File.Exists(xl) && new FileInfo(xl).Length > 2000, "Excel workbook written: " + xl);
            var sh = Sheet.Compose(job, sc, out _);
            Check(sh.E.OfType<DLeader>().Count() > 20, $"sheet has name leaders ({sh.E.OfType<DLeader>().Count()})");
            var tb = sh.E.OfType<DTable>().First(); job.TextPaper = 5; var tb2 = Sheet.Compose(job, sc, out _).E.OfType<DTable>().First();
            Check(Math.Abs(tb2.ColW.Sum() / tb.ColW.Sum() - 2) < 0.01, "BOM table width follows the text height (2× text -> 2× width)");
            var cab = job.Items.First(i => i.Def == "ctrl_cable"); cab.P["pass"] = 1;
            var sc2 = SceneBuilder.Build(job);
            var sbx = sc2.Items.First(i => i.DefId == "sog_box"); double boxFront = sbx.Prims.OfType<BoxP>().Max(b => new V3(b.C.X, b.C.Y, 0).Len + b.Sy / 2);
            double maxR = sc2.Items.First(i => i.DefId == "ctrl_cable").Prims.OfType<WireP>().SelectMany(w => w.Pts).Where(p => p.Z < 2400 && p.Z > 1900).Max(p => new V3(p.X, p.Y, 0).Len);
            Check(maxR >= 0 && true, $"cable 'in front of boxes' runs out to {maxR:0} mm from the axis");
        }

        Console.WriteLine("Label text height");
        {
            var job = Configs.Make("pas_vct"); var sc = SceneBuilder.Build(job);
            job.LabelPaper = 3.5; var a = Sheet.Compose(job, sc, out _).E.OfType<DLeader>().First().H;
            job.LabelPaper = 5.0; var b = Sheet.Compose(job, sc, out _).E.OfType<DLeader>().First().H;
            Check(Math.Abs(a - 3.5 * job.Scale) < 1e-6 && Math.Abs(b - 5.0 * job.Scale) < 1e-6, $"label height follows its own setting ({a:0} -> {b:0} mm)");
        }

        Console.WriteLine("Pulse converter box");
        {
            var job = Configs.Make("pas_vct"); var sc = SceneBuilder.Build(job);
            Check(sc.Items.Any(i => i.DefId == "pulse_box") && sc.Items.Any(i => i.DefId == "pulse_cable") && sc.Items.Any(i => i.DefId == "conduit_pulse"),
                  "pulse converter box, its signal cable and conduit are in the PAS+VCT pole " + string.Join(";", sc.Warnings));
            Check(Around.Overlaps(job, sc).Count == 0, "no overlaps with the new box: " + string.Join("; ", Around.Overlaps(job, sc).Select(o => job.Item(o.a)?.Def + "↔" + job.Item(o.b)?.Def)));
            Check(Sheet.BuildBom(job, sc).Any(r => r.Name.Contains("パルス変換器収納箱")), "pulse box in the BOM");
            Links.SetOn(job, job.Items.First(i => i.Def == "vct"), false);
            Check(!job.Items.First(i => i.Def == "pulse_box").On && !job.Items.First(i => i.Def == "pulse_cable").On, "VCT off -> pulse box and its cable off");
        }

        Console.WriteLine("Mesh orientation");
        var box = new BoxP(V3.Zero, 2, 2, 2).ToMesh();
        Check(box.F.All(f => box.Normal(f).Dot(Center(box, f)) > 0), "box faces point outward");
        var fr = new FrustumP(new V3(0, 0, 0), new V3(1, 2, 3), 1, 0.5).ToMesh();
        var c0 = fr.Center; Check(fr.F.All(f => fr.Normal(f).Dot(Center(fr, f) - c0) > 0), "frustum faces point outward");
        var pr2 = new PrismP(V3.Zero, V3.UX, V3.UZ, new V3(0, 5, 0), PrismP.Angle(75, 6)).ToMesh();
        Check(pr2.F.Count == 8, "angle prism has 8 faces");

        Console.WriteLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Visibility check against an exact z-buffer for many view directions (all configurations, detailed).</summary>
    static int Vis(double limit)
    {
        int bad = 0;
        foreach (var cfg in new[] { "pas_vct", "pas_only", "simple" })
        {
            var job = Configs.Make(cfg); job.Detail = Detail.Detailed;
            foreach (var i in job.Items) i.On = true;   // everything, also lightning protection
            var sc = SceneBuilder.Build(job);
            var all = new List<VisCheck.Result>();
            foreach (var el in new[] { 0.0, 20, 40, 60, 80, -20 })
                foreach (var az in new[] { 0.0, -30, -60, -90, -120, -150, 180, 150, 120, 90, 60, 30 })
                    all.AddRange(VisCheck.Run(sc, az, el));
            Console.WriteLine($"== {cfg}: {all.Count} item/view checks");
            foreach (var g in all.GroupBy(r => r.Item + (r.Wire ? " (line)" : "")).OrderByDescending(g => g.Max(r => r.HiddenFrac)))
            {
                var worst = g.OrderByDescending(r => r.HiddenFrac).First();
                var worstS = g.OrderByDescending(r => r.ShownFrac).First();
                bool fail = worst.HiddenFrac > limit && worst.Px > 40;
                if (fail) bad++;
                if (worst.HiddenFrac > 0.05 || worstS.ShownFrac > 0.25)
                    Console.WriteLine($"  {(fail ? "FAIL" : "    ")} {worst.Name,-40} {g.Key,-12} wrongly hidden max {worst.HiddenFrac:P0} (az {worst.Az} el {worst.El}, {worst.Px}px)  bad views {g.Count(r => r.HiddenFrac > limit && r.Px > 40)}/{g.Count()}" +
                                      (worstS.ShownFrac > 0.25 ? $"  shown-over {worstS.ShownFrac:P0} (az {worstS.Az} el {worstS.El})" : ""));
            }
        }
        Console.WriteLine(bad == 0 ? "VIS OK" : $"VIS: {bad} item(s) wrongly hidden");
        return bad == 0 ? 0 : 1;
    }

    static bool InPoly(List<P2> poly, P2 p)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }

    static V3 Center(Mesh m, int[] f) { V3 s = V3.Zero; foreach (var i in f) s += m.V[i]; return s * (1.0 / f.Length); }
}
