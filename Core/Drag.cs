using System;
using System.Collections.Generic;
using System.Linq;

namespace PoleToolApp.Core
{
    /// <summary>
    /// Rules for moving equipment by dragging it in the front or side preview: vertical movement changes the height
    /// (clamped to an allowed range), crossing the pole axis changes the side, some items change a distance instead.
    /// </summary>
    public static class Drag
    {
        /// <summary>Snapshot taken when the drag starts.</summary>
        public sealed class Start
        {
            public EquipItem Item; public string Part;   // Part: "h" height, "dist" guy anchor / handhole distance, "len" duct length
            public double Z0, Side0; public Dictionary<string, double> P0; public P2 Grab;
            /// <summary>Space taken by other elements around the pole (null = no collision check).</summary>
            public Around.Space Space;
        }

        static readonly HashSet<string> NotDraggable = new HashSet<string>
        { "jumper", "hv_cable", "service_line", "pipe_band", "bolt_conn", "link_cable" };

        /// <summary>Cable runs along the pole: they can only be turned around the pole, not raised.</summary>
        static readonly HashSet<string> NoHeight = new HashSet<string> { "ctrl_cable", "meter_cable", "earth" };

        static string Tpl(EquipItem it) => Catalog.Find(it.Def)?.Template;

        /// <summary>The item that really moves when 'itemId' is grabbed (insulators move with their arm, guy parts with the guy …).</summary>
        public static EquipItem Target(PoleJob job, string itemId)
        {
            var it = job.Items.FirstOrDefault(i => i.Id == itemId);
            if (it == null) return null;
            var def = Catalog.Find(it.Def); if (def == null) return null;
            if (def.Template == "part_of" || def.Template == "deadend_ins" || def.Template == "pin_ins" || def.Template == "cable_head"
                || def.Template == "la" || def.Template == "band" || def.Template == "arm_tie" || (def.Template == "vct" && Host(job, it) != null))
                return Host(job, it) ?? (def.Template == "part_of" ? null : it);
            if (NotDraggable.Contains(def.Template)) return null;
            return it;
        }

        static EquipItem Host(PoleJob job, EquipItem it)
        {
            if (!string.IsNullOrEmpty(it.At)) { var h = job.Item(it.At); if (h != null && h.On) return h; }
            var hd = Catalog.Find(it.Def)?.AtDef;
            return hd == null ? null : job.Items.FirstOrDefault(i => i.On && i.Def == hd);
        }

        /// <summary>Z of the item's reference point as used by the drag (height or "top" parameter).</summary>
        static double GetZ(PoleJob job, EquipItem it) => Tpl(it) == "conduit" ? it.Get("top", 2400) : job.Z(it);

        static void SetZ(PoleJob job, EquipItem it, double z)
        {
            if (Tpl(it) == "conduit") { it.P["top"] = z; return; }
            it.H = it.Ref == HRef.Top ? job.Pole.Top - z : z;
        }

        public static Start Begin(PoleJob job, string itemId, P2 grab, Scene scene = null)
        {
            var it = Target(job, itemId); if (it == null) return null;
            string t = Tpl(it);
            if ((t == "ctrl_cable" || t == "meter_cable") && it.Get("fixedSide", 0) < 0.5)
            {   // the run had an automatic side so far: start from where it is drawn now
                scene ??= SceneBuilder.Build(job);
                var f = scene.Items.Where(x => x.ItemId == it.Id).SelectMany(x => Around.Of(x, job.Pole, job)).OrderByDescending(x => x.Z1 - x.Z0).FirstOrDefault();
                if (f != null) it.Side = Math.Round(f.A, 1);
                it.P["fixedSide"] = 1;
            }
            var s = new Start { Item = it, Z0 = GetZ(job, it), Side0 = it.Side, P0 = new Dictionary<string, double>(it.P ?? new()), Grab = grab, Part = "h" };
            if (Sided.Contains(t))
            {
                scene ??= SceneBuilder.Build(job);
                var with = new List<string>();
                if (t == "riser_pipe" || t == "ug_pipe")
                    with.AddRange(job.Items.Where(o => o.On && (Tpl(o) == "hv_cable" || Tpl(o) == "riser_pipe" || Tpl(o) == "ug_pipe" || Tpl(o) == "handhole")).Select(o => o.Id));
                with.AddRange(Links.MountedOn(job, it).Select(o => o.Id));
                s.Space = Around.Prepare(job, scene, it, s.Side0, s.Z0, with);
            }
            if (t == "guy" && grab.Y < job.Z(it) * 0.4) s.Part = "dist";
            if (t == "handhole") s.Part = "dist";
            return s;
        }

        /// <summary>Allowed height range of an item (mm above GL).</summary>
        public static (double lo, double hi) Range(PoleJob job, EquipItem it)
        {
            double top = job.Pole.Top, bot = job.Pole.Bottom;
            switch (Tpl(it))
            {
                case "neka": return (bot + 300, -200);
                case "earth_rod": return (-3000, -300);
                case "ug_pipe": return (Math.Max(bot + 200, -3000), -300);
                case "guy": return (Math.Min(2000, top - 100), top - 100);
                case "conduit": return (500, top - 300);
                case "riser_pipe": return (1500, top - 300);
                case "step_bolts": return (1800, top - 600);   // first step bolt at least 1.8 m above GL
                case "arm": return (1500, top - 100);
                case "deadend_fit": return (top - 1000, top);
                case "lrod": return (0, 0);
                default: return (300, top - 50);
            }
        }

        static readonly HashSet<string> Sided = new HashSet<string>
        { "arm", "pas", "vct", "box", "cyl", "riser_pipe", "conduit", "guy", "earth_rod", "handhole", "ug_pipe", "deadend_fit", "step_bolts", "lrod",
          "ctrl_cable", "meter_cable", "earth" };

        /// <summary>Front (true) / side (false) view shortcut, used by tests.</summary>
        public static string Apply(PoleJob job, Start s, P2 now, bool front, double snap = 10) =>
            Apply(job, s, now, front ? View3.Front : View3.Side, snap < 5);

        /// <summary>
        /// Applies the drag. 'now' and the grab point are in the view's own model coordinates (pole axis at x = 0).
        /// heightOnly (Shift): side locked. rotate (Ctrl+Shift): free angle around the pole, 15° steps.
        /// fine (Alt): 1 mm / 1° steps.
        /// </summary>
        public static string Apply(PoleJob job, Start s, P2 now, View3 view, bool fine = false, bool heightOnly = false, bool rotate = false)
        {
            var it = s.Item; string t = Tpl(it);
            double snap = fine ? 1 : 10, angSnap = fine ? 1 : 15;
            bool elev = Math.Abs(view.El) < 1e-6;   // any horizontal view (front, side or turned around the pole)
            double sRight = Deg(view.Right), sFace = Deg(view.Toward);
            double thr = job.Pole.RadiusAt(0) + 150;
            bool noH = NoHeight.Contains(t);
            // ----- new height (only applied further down)
            var (lo, hi) = Range(job, it);
            double up = Math.Max(0.3, view.Up.Z);   // a vertical metre looks shorter when seen from above
            double z = noH || s.Part == "dist" || rotate ? s.Z0 : s.Z0 + (now.Y - s.Grab.Y) / up;
            if (!noH && s.Part != "dist") z = Math.Round(Math.Max(lo, Math.Min(hi, z)) / snap) * snap;
            // ----- side
            double side = s.Side0;
            if (Sided.Contains(t))
            {
                if (rotate)
                {
                    side = s.Side0 + (now.X - s.Grab.X) * 0.05;   // 1 m of mouse travel (model) = 50°
                    side = ((Math.Round(side / angSnap) * angSnap) % 360 + 360) % 360;
                }
                else if (!heightOnly && elev)
                {
                    side = now.X > thr ? sRight : now.X < -thr ? Norm(sRight + 180) : (Near(s.Side0, sFace) || Near(s.Side0, sFace + 180) ? s.Side0 : sFace);
                    if ((t == "guy" || t == "handhole" || t == "ug_pipe") && Math.Abs(now.X) <= thr) side = s.Side0;
                }
                else if (noH && !elev && !heightOnly)
                {   // cables in a 3D view: horizontal mouse movement turns them
                    side = Norm(s.Side0 + (now.X - s.Grab.X) * 0.05);
                }
                // ----- keep clear of other pipes, cables and boxes: stop side by side, jump over when pulled further
                if (s.Space != null && s.Space.Own.Count > 0)
                {
                    int dir = Math.Sign(((side - s.Side0) % 360 + 540) % 360 - 180);
                    side = Around.Nearest(Around.Blocked(s.Space, z - s.Z0), side, dir, fine ? 0.5 : 1);
                }
            }
            it.Side = side;
            if (t == "riser_pipe" || t == "ug_pipe")   // the cable route follows
                foreach (var o in job.Items.Where(o => o.On && (Tpl(o) == "hv_cable" || Tpl(o) == "riser_pipe" || Tpl(o) == "ug_pipe" || Tpl(o) == "handhole")))
                    o.Side = side;
            if (rotate || noH) return $"{Name(it)}: {side:0.#}°";

            if (s.Part == "dist")
            {
                double d;
                double a = side * Math.PI / 180; var dir = view.P(new V3(Math.Sin(a), -Math.Cos(a), 0));
                if (elev && Math.Abs(dir.X) > 0.2) d = now.X / dir.X;   // absolute position along the item direction
                else
                {   // move along the projected direction of the item
                    double l2 = dir.X * dir.X + dir.Y * dir.Y;
                    double d0 = s.P0.TryGetValue("dist", out var dv) ? dv : 5000;
                    d = l2 < 0.04 ? d0 : d0 + ((now.X - s.Grab.X) * dir.X + (now.Y - s.Grab.Y) * dir.Y) / l2;
                }
                d = Math.Round(Math.Max(1500, Math.Min(20000, d)) / snap) * snap;
                it.P["dist"] = d;
                return $"{Name(it)}: {d:0} mm, {side:0}°";
            }
            SetZ(job, it, z);
            return $"{Name(it)}: GL{(z >= 0 ? "+" : "")}{z:0} mm" + (Sided.Contains(t) ? $", {side:0}°" : "") + $"   ({lo:0} … {hi:0})";
        }

        /// <summary>Puts the item back to the state at drag start (Esc).</summary>
        public static void Cancel(PoleJob job, Start s)
        {
            SetZ(job, s.Item, s.Z0); s.Item.Side = s.Side0; s.Item.P = new Dictionary<string, double>(s.P0);
        }

        /// <summary>Side angle (0 = front −Y, 90 = +X) of a horizontal direction.</summary>
        static double Deg(V3 v) => Norm(Math.Round(Math.Atan2(v.X, -v.Y) * 180 / Math.PI, 3));
        static double Norm(double a) => ((a % 360) + 360) % 360;

        static bool Near(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180) < 1;
        static string Name(EquipItem it) { var d = Catalog.Find(it.Def); return d == null ? it.Def : (d.Jp + " / " + d.En); }
    }
}
