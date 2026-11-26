using System;
using System.Collections.Generic;
using System.Linq;

namespace PoleToolApp.Core
{
    /// <summary>Ready-made pole configurations (start points; everything can be changed afterwards).</summary>
    public static class Configs
    {
        /// <summary>Folder of named user configurations (set by the host, e.g. %APPDATA%\PoleTool\configs).</summary>
        public static string UserDir;

        static string FileOf(string name)
        {
            var bad = System.IO.Path.GetInvalidFileNameChars();
            var safe = new string((name ?? "").Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray());
            return System.IO.Path.Combine(UserDir, (safe.Length == 0 ? "config" : safe) + ".polejob.json");
        }

        /// <summary>Names of the saved user configurations, sorted.</summary>
        public static List<string> UserNames()
        {
            var l = new List<string>();
            if (UserDir == null || !System.IO.Directory.Exists(UserDir)) return l;
            foreach (var f in System.IO.Directory.GetFiles(UserDir, "*.polejob.json"))
            {
                var j = PoleJob.FromJson(System.IO.File.ReadAllText(f));
                var n = System.IO.Path.GetFileName(f); n = n.Substring(0, n.Length - ".polejob.json".Length);
                l.Add(string.IsNullOrWhiteSpace(j?.ConfigName) ? n : j.ConfigName);
            }
            l.Sort(StringComparer.CurrentCultureIgnoreCase);
            return l;
        }

        public static bool UserExists(string name) => UserDir != null && System.IO.File.Exists(FileOf(name));

        /// <summary>Saves the complete job (pole, equipment, drawing settings) under a name.</summary>
        public static void SaveUser(string name, PoleJob job)
        {
            System.IO.Directory.CreateDirectory(UserDir);
            var j = job.Clone(); j.ConfigName = name.Trim();
            System.IO.File.WriteAllText(FileOf(name), j.ToJson(true));
        }

        public static PoleJob LoadUser(string name) =>
            UserExists(name) ? PoleJob.FromJson(System.IO.File.ReadAllText(FileOf(name))) : null;

        public static void DeleteUser(string name) { if (UserExists(name)) System.IO.File.Delete(FileOf(name)); }

        public static readonly (string id, string jp, string en)[] List =
        {
            ("pas_vct", "構内第一柱（PAS＋VCT・架空引込→地中）", "First pole: PAS + VCT, overhead in, cable down"),
            ("pas_only", "構内第一柱（PAS・地中ケーブル）", "First pole: PAS only, cable down"),
            ("simple", "簡易図（主要設備レイアウト用）", "Simple layout drawing"),
            ("empty", "柱のみ", "Pole only"),
        };

        public static PoleJob Make(string id, PoleJob keepPole = null)
        {
            var j = new PoleJob();
            if (keepPole != null) { j.Pole = keepPole.Clone().Pole; j.LabelsJp = keepPole.LabelsJp; j.Scale = keepPole.Scale; j.LayerPrefix = keepPole.LayerPrefix; j.WipeoutLayer = keepPole.WipeoutLayer; }
            string[] defs;
            switch (id)
            {
                case "pas_vct":
                    j.Detail = Detail.Detailed;
                    defs = new[] { "svc_line", "deadend_arm", "deadend_fit", "deadend_ins", "strong_band", "pas", "arm_vct", "arm_band", "arm_tie", "vct", "pin_ins",
                                   "bolt_conn", "jumper", "arm_head", "cable_head", "riser_pipe", "hv_cable", "ctrl_cable", "meter_cable", "conduit_ctrl",
                                   "conduit_ntt", "conduit_meter", "pipe_band", "sog_box", "pas_rbox", "meter_box", "pulse_box", "pulse_cable", "conduit_pulse", "guy", "guy_ins", "guy_guard", "guy_anchor",
                                   "guy_grip", "earth_a", "earth_d", "earth_rod", "neka", "base_plate", "ug_pipe", "step_bolts" };
                    break;
                case "pas_only":
                    j.Detail = Detail.Standard;
                    defs = new[] { "svc_line", "deadend_arm", "deadend_ins", "pas", "jumper", "arm_head", "cable_head", "riser_pipe", "hv_cable", "ctrl_cable",
                                   "conduit_ctrl", "pipe_band", "sog_box", "guy", "guy_ins", "guy_guard", "guy_anchor", "earth_a", "earth_rod", "neka", "step_bolts" };
                    break;
                case "simple":
                    j.Detail = Detail.Sketch; j.Bom = false; j.Dims = false; j.ViewSide = false;
                    defs = new[] { "svc_line", "deadend_arm", "deadend_ins", "pas", "arm_head", "cable_head", "jumper", "riser_pipe", "hv_cable", "sog_box", "guy" };
                    break;
                default:
                    defs = new string[0]; break;
            }
            foreach (var d in defs) j.AddFromCatalog(d);
            if (id != "empty")
            {   // lightning protection: listed but off (PAS normally has a built-in arrester) – tick to use
                foreach (var d in new[] { "la", "lrod", "earth_lp" }) { var x = j.AddFromCatalog(d); x.On = false; }
            }
            if (id == "pas_only")
            {
                // without VCT the cable-head arm sits higher
                var arm = j.Items.Find(i => i.Def == "arm_head"); if (arm != null) arm.H = 1600;
                var heads = j.Items.Find(i => i.Def == "cable_head"); if (heads != null) heads.H = 1600;
                var riser = j.Items.Find(i => i.Def == "riser_pipe"); if (riser != null) riser.H = 2800;
            }
            if (id == "simple")
            {
                var arm = j.Items.Find(i => i.Def == "arm_head"); if (arm != null) arm.H = 1600;
                var riser = j.Items.Find(i => i.Def == "riser_pipe"); if (riser != null) riser.H = 2800;
            }
            j.Normalize();
            try { Around.Arrange(j); } catch { }   // nothing on top of each other around the pole
            return j;
        }
    }
}
