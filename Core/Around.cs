using System;
using System.Collections.Generic;
using System.Linq;

namespace PoleToolApp.Core
{
    /// <summary>
    /// Space around the pole: which angular sectors (at which heights) are taken by pipes, cables, boxes, step bolts …
    /// Used while turning / moving an element so it stops side by side with others (keeping a minimum gap)
    /// instead of sitting on top of them; dragged further it jumps over to the next free place.
    /// </summary>
    public static class Around
    {
        /// <summary>Footprint of one part on the pole surface: height range and angular sector (centre A, half width H, degrees).</summary>
        public sealed class Foot { public double Z0, Z1, A, H; public string Item; public Kind K; public bool AvoidBoxes; }

        public enum Kind { Pipe, Box, Wire, Bolt }

        /// <summary>Which kinds may not share the same place. Cables may run behind boxes (boxes stand off the pole on
        /// brackets) and past step bolts; everything else needs its own place.</summary>
        static bool Conflicts(Foot x, Foot y)
        {
            if (x.K == Kind.Box && y.K == Kind.Wire) return y.AvoidBoxes;
            if (y.K == Kind.Box && x.K == Kind.Wire) return x.AvoidBoxes;
            return Conflicts(x.K, y.K);
        }

        static bool Conflicts(Kind a, Kind b)
        {
            if (a > b) (a, b) = (b, a);
            if (a == Kind.Box && b == Kind.Wire) return false;
            if (a == Kind.Wire && b == Kind.Bolt) return false;
            if (a == Kind.Bolt && b == Kind.Bolt) return false;
            return true;
        }
        static double GapOf(Kind a, Kind b) => a == Kind.Wire && b == Kind.Wire ? 15 : a == Kind.Wire || b == Kind.Wire ? 20 : 30;

        static Kind KindOf(string defId)
        {
            switch (Catalog.Find(defId)?.Template)
            {
                case "conduit": case "riser_pipe": case "ug_pipe": return Kind.Pipe;
                case "ctrl_cable": case "meter_cable": case "earth": case "link_cable": return Kind.Wire;
                case "step_bolts": return Kind.Bolt;
                default: return Kind.Box;
            }
        }

        /// <summary>Only geometry within this distance (mm) of the pole surface counts as "on the pole".</summary>
        const double Reach = 300;

        static double Ang(double x, double y) => Norm(Math.Atan2(x, -y) * 180 / Math.PI);
        static double Norm(double a) => ((a % 360) + 360) % 360;
        static double Diff(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);

        /// <summary>Footprints of a scene item (rings around the pole and parts away from the pole are ignored).</summary>
        public static List<Foot> Of(SceneItem si, PoleSpec pole, PoleJob job = null)
        {
            var list = new List<Foot>();
            foreach (var p in si.Prims)
            {
                if (p is FrustumP f && Math.Abs(f.A.X) < 1 && Math.Abs(f.A.Y) < 1 && Math.Abs(f.B.X) < 1 && Math.Abs(f.B.Y) < 1) continue;   // pole, rings, cap
                if (p is WireP w)
                {   // only the run along the pole surface; the hop from the equipment to the pole does not count
                    for (int k = 0; k + 1 < w.Pts.Count; k++)
                    {
                        var a = w.Pts[k]; var b = w.Pts[k + 1];
                        bool onPole(V3 q) => Math.Sqrt(q.X * q.X + q.Y * q.Y) < pole.RadiusAt(Math.Max(0, q.Z)) + 80;
                        if (onPole(a) && onPole(b)) Add(list, new[] { a, b }, pole, 12, si.ItemId);
                    }
                    continue;
                }
                var m = p.ToMesh(); if (m == null) continue;
                Add(list, m.V, pole, 0, si.ItemId);
            }
            var k0 = KindOf(si.DefId); bool avoid = job?.Item(si.ItemId)?.Get("pass", 0) >= 1.5;
            foreach (var f in list) { f.K = k0; f.AvoidBoxes = avoid; }
            return list;
        }

        static void Add(List<Foot> list, IList<V3> pts, PoleSpec pole, double thick, string item)
        {
            var near = new List<V3>();
            foreach (var q in pts)
            {
                double r = Math.Sqrt(q.X * q.X + q.Y * q.Y);
                if (r > 1 && r < pole.RadiusAt(Math.Max(0, q.Z)) + Reach) near.Add(q);
            }
            if (near.Count == 0) return;
            // smallest arc that contains all angles: complement of the largest gap
            var a = near.Select(q => Ang(q.X, q.Y)).OrderBy(v => v).ToList();
            double gap = 360 - a[a.Count - 1] + a[0]; int gi = a.Count - 1;
            for (int i = 0; i + 1 < a.Count; i++) if (a[i + 1] - a[i] > gap) { gap = a[i + 1] - a[i]; gi = i; }
            double start = a[(gi + 1) % a.Count], arc = 360 - gap;
            if (arc > 120) return;   // goes through / around the pole (through bolts …): not a side element
            double zr = near.Average(q => pole.RadiusAt(Math.Max(0, q.Z)));
            double h = arc / 2 + Math.Asin(Math.Min(1, thick / 2 / zr)) * 180 / Math.PI;
            // only the part above ground counts (below GL pipes bend away)
            double z0 = Math.Max(0, near.Min(q => q.Z)), z1 = Math.Min(pole.Top + 100, near.Max(q => q.Z));
            if (z1 <= z0) return;
            list.Add(new Foot { Z0 = z0, Z1 = z1, A = Norm(start + arc / 2), H = h, Item = item });
        }

        /// <summary>Parts that sit on or in other parts (pipe bands, connectors, the HV cable inside its riser …).</summary>
        static readonly HashSet<string> Ignored = new HashSet<string>
        { "arm", "band", "deadend_ins", "service_line", "jumper", "pipe_band", "bolt_conn", "hv_cable", "arm_tie", "pin_ins", "cable_head", "la", "vct" };

        /// <summary>Items that are meant to touch (a cable and the boxes it connects, a conduit and its box …).</summary>
        static readonly (string a, string b)[] Joined =
        {
            ("ctrl_cable", "pas"), ("ctrl_cable", "sog_box"), ("ctrl_cable", "pas_rbox"), ("ctrl_cable", "conduit_ctrl"),
            ("meter_cable", "vct"), ("meter_cable", "meter_box"), ("meter_cable", "conduit_meter"),
            ("earth_a", "pas"), ("earth_a", "la"), ("earth_d", "pas"), ("earth_lp", "lrod"), ("earth_lp", "la"),
            ("conduit_ctrl", "sog_box"), ("conduit_meter", "meter_box"), ("riser_pipe", "cable_head"),
            ("pas_rbox", "pas"), ("pas_rbox", "sog_box"),
            ("pulse_cable", "meter_box"), ("pulse_cable", "pulse_box"), ("pulse_cable", "conduit_pulse"), ("pulse_cable", "conduit_meter"),
            ("pulse_box", "meter_box"),
        };

        public static bool Touching(PoleJob job, string idA, string idB)
        {
            var a = job.Item(idA); var b = job.Item(idB); if (a == null || b == null) return false;
            if (a.At == b.Id || b.At == a.Id) return true;
            var da = Catalog.Find(a.Def); var db = Catalog.Find(b.Def);
            if (da?.AtDef == b.Def || db?.AtDef == a.Def) return true;
            foreach (var (x, y) in Joined) if ((a.Def == x && b.Def == y) || (a.Def == y && b.Def == x)) return true;
            return false;
        }

        static bool Skip(SceneItem si) => si.DefId == "pole" || Ignored.Contains(Catalog.Find(si.DefId)?.Template ?? "");

        /// <summary>Everything a moving item must keep clear of, prepared once at the start of a drag.</summary>
        public sealed class Space
        {
            public List<Foot> Own = new List<Foot>(), Others = new List<Foot>();
            public double Side0, Z0, GapMm = 30;
            public PoleSpec Pole;
        }

        /// <summary>Collects the moving item's own footprints (relative to its side / height) and those of all other items.</summary>
        public static Space Prepare(PoleJob job, Scene scene, EquipItem moving, double side0, double z0, IEnumerable<string> movesWith)
        {
            var sp = new Space { Side0 = side0, Z0 = z0, Pole = job.Pole };
            var with = new HashSet<string>(movesWith ?? Enumerable.Empty<string>()) { moving.Id };
            foreach (var si in scene.Items)
            {
                if (si.ItemId == moving.Id) { if (si.DefId != "pole") sp.Own.AddRange(Of(si, job.Pole, job)); continue; }
                if (Skip(si) || with.Contains(si.ItemId) || Touching(job, moving.Id, si.ItemId)) continue;
                sp.Others.AddRange(Of(si, job.Pole, job));
            }
            return sp;
        }

        /// <summary>Angular intervals (degrees, may wrap) where the item may NOT be placed when its height is shifted by dz.</summary>
        public static List<(double lo, double hi)> Blocked(Space sp, double dz)
        {
            var res = new List<(double, double)>();
            foreach (var o in sp.Own)
                foreach (var b in sp.Others)
                {
                    double z0 = o.Z0 + dz, z1 = o.Z1 + dz;
                    if (!Conflicts(o, b)) continue;
                    if (Math.Min(z1, b.Z1) - Math.Max(z0, b.Z0) <= 20) continue;   // no height overlap (20 mm tolerance)
                    double zm = (Math.Max(z0, b.Z0) + Math.Min(z1, b.Z1)) / 2;
                    double gapA = Math.Asin(Math.Min(1, GapOf(o.K, b.K) / Math.Max(50, sp.Pole.RadiusAt(zm)))) * 180 / Math.PI;
                    double half = o.H + b.H + gapA;
                    // item side s puts this part at angle o.A + (s - Side0); blocked when it is within 'half' of b.A
                    double c = Norm(b.A - (o.A - sp.Side0));
                    res.Add((c - half, c + half));
                }
            return res;
        }

        /// <summary>Free angle nearest to 'desired'; on a tie the one on the side the item comes from ('dir' = direction of movement ±1),
        /// so it stops beside an obstacle and only jumps over it once pulled past the obstacle's middle.</summary>
        public static double Nearest(List<(double lo, double hi)> blocked, double desired, int dir = 0, double step = 0.5)
        {
            bool Free(double s) { foreach (var (lo, hi) in blocked) if (Diff(s, (lo + hi) / 2) < (hi - lo) / 2) return false; return true; }
            if (Free(desired)) return Norm(desired);
            for (double d = step; d <= 180; d += step)
            {
                double a = desired - d * (dir >= 0 ? 1 : -1), b = desired + d * (dir >= 0 ? 1 : -1);
                if (Free(a)) return Norm(Math.Round(a / step) * step);
                if (Free(b)) return Norm(Math.Round(b / step) * step);
            }
            return Norm(desired);   // no free place at all: leave it
        }

        /// <summary>Templates that "Arrange" may turn (pipes, boxes, cable runs along the pole).</summary>
        static readonly HashSet<string> Arrangeable = new HashSet<string> { "conduit", "riser_pipe", "box", "cyl", "ctrl_cable", "meter_cable", "earth", "step_bolts" };
        // note: the pulse signal cable (link_cable) follows its boxes and conduit, it is not arranged itself

        /// <summary>
        /// Moves overlapping pipes / boxes / cable runs to the nearest free place around the pole (big items first,
        /// each keeps its direction as far as possible). Returns the number of items moved.
        /// </summary>
        public static int Arrange(PoleJob job)
        {
            int moved = 0;
            var order = job.Items.Where(i => i.On && Arrangeable.Contains(Catalog.Find(i.Def)?.Template ?? ""))
                .OrderBy(i => { var t = Catalog.Find(i.Def).Template; return t == "riser_pipe" ? 0 : t == "box" || t == "cyl" ? 1 : t == "conduit" ? 2 : t == "step_bolts" ? 3 : 4; })
                .ToList();
            var placed = new HashSet<string>();
            foreach (var it in order)
            {
                var scene = SceneBuilder.Build(job);
                string t = Catalog.Find(it.Def).Template;
                if ((t == "ctrl_cable" || t == "meter_cable") && it.Get("fixedSide", 0) < 0.5)
                {
                    var f = scene.Items.Where(x => x.ItemId == it.Id).SelectMany(x => Of(x, job.Pole, job)).OrderByDescending(x => x.Z1 - x.Z0).FirstOrDefault();
                    if (f == null) continue;
                    it.Side = Math.Round(f.A, 1); it.P["fixedSide"] = 1;
                }
                // only items already placed (and everything that is not arrangeable) count as obstacles
                var sp = Prepare(job, scene, it, it.Side, job.Z(it), job.Items.Where(o => o != it && Arrangeable.Contains(Catalog.Find(o.Def)?.Template ?? "") && !placed.Contains(o.Id)).Select(o => o.Id));
                placed.Add(it.Id);
                if (sp.Own.Count == 0) continue;
                double s = Nearest(Blocked(sp, 0), it.Side, 0, 1);
                if (Diff(s, it.Side) > 0.01)
                {
                    it.Side = s; moved++;
                    if (t == "riser_pipe")
                        foreach (var o in job.Items.Where(o => o.On && (o.Def == "hv_cable" || o.Def == "ug_pipe" || o.Def == "handhole"))) o.Side = s;
                }
            }
            return moved;
        }

        /// <summary>Pairs of items whose footprints overlap (for a warning after building).</summary>
        public static List<(string a, string b)> Overlaps(PoleJob job, Scene scene)
        {
            var feet = new List<Foot>();
            foreach (var si in scene.Items) if (!Skip(si)) feet.AddRange(Of(si, job.Pole, job));
            var res = new HashSet<(string, string)>();
            for (int i = 0; i < feet.Count; i++)
                for (int j = i + 1; j < feet.Count; j++)
                {
                    var a = feet[i]; var b = feet[j]; if (a.Item == b.Item || !Conflicts(a, b) || Touching(job, a.Item, b.Item)) continue;
                    if (Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0) <= 20) continue;
                    if (Diff(a.A, b.A) < a.H + b.H - 0.5) res.Add(string.CompareOrdinal(a.Item, b.Item) < 0 ? (a.Item, b.Item) : (b.Item, a.Item));
                }
            return res.ToList();
        }
    }
}
