using System;
using System.Collections.Generic;
using System.Linq;

namespace PoleToolApp.Core
{
    /// <summary>Builds the 3D scene (pole + equipment + cables) from a job. All dimensions in mm, Z up, GL = 0.</summary>
    public sealed class SceneBuilder
    {
        readonly PoleJob J; readonly PoleSpec Pole; readonly Detail Det; readonly Scene S = new Scene();
        readonly int Seg;

        sealed class Arm
        {
            public double Z, A, Len; public V3 Center, Axis, Out; public bool OneSided;
            /// <summary>Point on the arm top. Centered arm: t in -0.5..0.5 along the arm; one-sided: t in 0..1 from the pole face.</summary>
            public V3 Top(double t) => (OneSided ? Center + Axis * ((t - 0.5) * Len) : Center + Axis * (t * Len)) + V3.UZ * (A / 2);
            public V3 Bottom(double t) => Top(t) - V3.UZ * A;
        }
        readonly Dictionary<string, Arm> _arms = new Dictionary<string, Arm>();
        readonly Dictionary<string, (V3 c, V3 dir, double w, double d, double h)> _boxes = new();
        List<V3> _strainEnds, _pasPower, _pasLoad, _pins, _vctIn, _vctOut, _headTops, _headBottoms, _laTops;
        V3? _pasBottom, _vctTerminal, _laBase, _rodBase;
        V3 _riserAxis; double _riserTop = double.NaN, _riserDia, _ugDepth = 900, _bendR = 600; V3 _ugDir = V3.UX; bool _hasRiser;
        V3? _ugStart;
        readonly List<(V3 axis, double top, double bottom, double dia)> _pipes = new();

        SceneBuilder(PoleJob job) { J = job; Pole = job.Pole; Det = job.Detail; Seg = Det == Detail.Sketch ? 12 : Det == Detail.Standard ? 20 : 28; }

        public static Scene Build(PoleJob job)
        {
            job.Normalize();
            var b = new SceneBuilder(job);
            try { b.Run(); } catch (Exception ex) { b.S.Warnings.Add("Build error: " + ex.Message); }
            return b.S;
        }

        // ------------------------------------------------------------------ helpers

        static V3 Dir(double side) { double a = side * Math.PI / 180; return new V3(Math.Sin(a), -Math.Cos(a), 0); }
        static V3 Tan(double side) { double a = side * Math.PI / 180; return new V3(Math.Cos(a), Math.Sin(a), 0); }
        double R(double z) => Pole.RadiusAt(Math.Min(z, Pole.Top));
        static V3 At(V3 horiz, double z) => new V3(horiz.X, horiz.Y, z);
        bool D2 => Det == Detail.Detailed;
        bool D0 => Det == Detail.Sketch;

        IEnumerable<EquipItem> On(string template) =>
            J.Items.Where(i => i.On && Catalog.Find(i.Def)?.Template == template);
        EquipItem FirstDef(string def) => J.Items.FirstOrDefault(i => i.On && i.Def == def);

        SceneItem NewItem(EquipItem it, string suffix = null)
        {
            var d = Catalog.Find(it.Def);
            var si = new SceneItem
            {
                ItemId = it.Id + (suffix ?? ""), DefId = it.Def,
                Jp = string.IsNullOrEmpty(it.LabelJp) ? d?.Jp : it.LabelJp,
                En = string.IsNullOrEmpty(it.LabelEn) ? d?.En : it.LabelEn,
                Spec = it.Spec, Qty = (int)Math.Round(it.Qty)
            };
            if (d != null) { si.BlockFront = NullIfEmpty(d.BlockFront); si.BlockSide = NullIfEmpty(d.BlockSide); si.Block3D = NullIfEmpty(d.Block3D); }
            S.Items.Add(si); return si;
        }
        static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        /// <summary>Sets the block base point of every item to the centre of its first solid (after building).</summary>
        void Origins()
        {
            foreach (var si in S.Items)
            {
                if (si.Origin.HasValue) continue;
                var m = si.Prims.FirstOrDefault(p => !(p is WireP))?.ToMesh();
                if (m != null) si.Origin = m.Center;
            }
        }

        static BoxP Box(V3 c, V3 ax, double sx, double sy, double sz)
        {
            ax = new V3(ax.X, ax.Y, 0).Unit;
            return new BoxP(c, sx, sy, sz) { Ax = ax, Ay = V3.UZ.Cross(ax), Az = V3.UZ };
        }

        /// <summary>Insulator body along an axis: core + sheds depending on detail.</summary>
        void Insulator(SceneItem si, V3 a, V3 axis, double len, double r, int sheds)
        {
            axis = axis.Unit;
            if (D0 || sheds <= 0) { si.Add(new FrustumP(a, a + axis * len, r, r * 0.8, Seg), Lyr.Equip); return; }
            si.Add(new FrustumP(a, a + axis * len, r * 0.45, r * 0.4, Seg), Lyr.Equip);
            int n = D2 ? sheds : Math.Max(1, sheds / 2);
            for (int k = 0; k < n; k++)
            {
                double t = (k + 0.5) / n; var c = a + axis * (len * t);
                si.Add(new FrustumP(c - axis * (len * 0.12 / n * 2), c + axis * (len * 0.05 / n * 2), r * 0.5, r, Seg), Lyr.Equip);
            }
        }

        void Ring(SceneItem si, double z, double width, double extra = 4, Lyr l = Lyr.Equip)
        {
            double r = R(z) + extra;
            si.Add(new FrustumP(new V3(0, 0, z - width / 2), new V3(0, 0, z + width / 2), r, r, Seg), l);
        }

        static List<V3> Bezier(V3 p, V3 c, V3 q, int n = 14)
        {
            var l = new List<V3>(n + 1);
            for (int i = 0; i <= n; i++) { double t = (double)i / n; l.Add(p * ((1 - t) * (1 - t)) + c * (2 * t * (1 - t)) + q * (t * t)); }
            return l;
        }

        /// <summary>Drooping jumper that keeps clear of the pole.</summary>
        List<V3> Jumper(V3 p, V3 q, double sagK = 0.18)
        {
            var mid = (p + q) * 0.5; double len = (q - p).Len;
            var h = new V3(mid.X, mid.Y, 0); double need = R(mid.Z) + 220;
            if (h.Len < need) h = (h.Len < 1 ? new V3(0, -1, 0) : h.Unit) * need;
            var c = new V3(h.X, h.Y, mid.Z - (len * sagK + 80));
            return Bezier(p, c, q);
        }

        Arm FindArm(EquipItem it)
        {
            if (!string.IsNullOrEmpty(it.At) && _arms.TryGetValue(it.At, out var a)) return a;
            var def = Catalog.Find(it.Def)?.AtDef;
            if (def == null) return null;
            var host = J.Items.FirstOrDefault(i => i.On && i.Def == def && _arms.ContainsKey(i.Id));
            return host == null ? null : _arms[host.Id];
        }

        // ------------------------------------------------------------------ main

        void Run()
        {
            S.PoleTop = Pole.Top; S.PoleBottom = Pole.Bottom;
            BuildPole();
            foreach (var it in On("arm")) BuildArm(it);
            foreach (var it in On("deadend_fit")) BuildDeadendFit(it);
            foreach (var it in On("deadend_ins")) BuildStrain(it);
            foreach (var it in On("service_line")) BuildService(it);
            foreach (var it in On("pas")) BuildPas(it);
            foreach (var it in On("vct")) BuildVct(it);
            foreach (var it in On("pin_ins")) BuildPins(it);
            foreach (var it in On("cable_head")) BuildHeads(it);
            foreach (var it in On("la")) BuildLa(it);
            foreach (var it in On("band")) BuildBand(it);
            foreach (var it in On("arm_tie")) BuildArmTie(it);
            foreach (var it in On("box")) BuildBox(it);
            foreach (var it in On("cyl")) BuildCyl(it);
            var hv = On("hv_cable").FirstOrDefault();
            var ug = On("ug_pipe").FirstOrDefault();
            if (ug != null) _ugDepth = Math.Max(300, -J.Z(ug));
            else if (hv != null) _ugDepth = Math.Max(300, hv.Get("depth", 900));
            _bendR = Math.Min(600, Math.Max(150, _ugDepth - 100));
            foreach (var it in On("riser_pipe")) BuildRiser(it, hv);
            foreach (var it in On("conduit")) BuildConduit(it);
            foreach (var it in On("jumper")) BuildJumpers(it);
            if (hv != null) BuildHvCable(hv);
            foreach (var it in On("ctrl_cable")) BuildCtrl(it);
            foreach (var it in On("meter_cable")) BuildMeterCable(it);
            foreach (var it in On("link_cable")) BuildLinkCable(it);
            foreach (var it in On("pipe_band")) BuildPipeBands(it);
            foreach (var it in On("guy")) BuildGuy(it);
            foreach (var it in On("lrod")) BuildLightningRod(it);
            foreach (var it in On("earth_rod")) BuildEarthRods(it);
            foreach (var it in On("earth")) BuildEarth(it);
            foreach (var it in On("neka")) BuildNeka(it);
            foreach (var it in On("base_plate")) BuildBasePlate(it);
            foreach (var it in On("ug_pipe")) BuildUgPipe(it);
            foreach (var it in On("handhole")) BuildHandhole(it);
            foreach (var it in On("step_bolts")) BuildSteps(it);
            foreach (var it in J.Items.Where(i => i.On && Catalog.Find(i.Def) == null))
                S.Warnings.Add($"Unknown equipment '{it.Def}' (not in the library) skipped.");
            Levels();
            Origins();
            foreach (var si in S.Items)
            {
                var src = J.Items.FirstOrDefault(i => i.Id == si.ItemId);
                if (src != null && src.Color > 0) foreach (var p in si.Prims) p.Color = src.Color;
            }
        }

        void Levels()
        {
            S.Levels.Add((Pole.Top, "柱頂", "Pole top"));
            foreach (var it in J.Items.Where(i => i.On && _arms.ContainsKey(i.Id)))
                S.Levels.Add((_arms[it.Id].Z, Catalog.Find(it.Def)?.Jp, Catalog.Find(it.Def)?.En));
            S.Levels.Add((0, "GL", "GL"));
            S.Levels.Add((Pole.Bottom, "柱底", "Pole bottom"));
        }

        // ------------------------------------------------------------------ pole

        void BuildPole()
        {
            var si = new SceneItem { ItemId = "pole", DefId = "pole", Jp = Pole.Kind == PoleKind.Steel ? "鋼管柱" : "コンクリート柱", En = Pole.Kind == PoleKind.Steel ? "Steel pole" : "Concrete pole",
                Spec = Pole.Designation(true), Qty = 1 };
            S.Items.Add(si);
            si.Add(new FrustumP(new V3(0, 0, Pole.Bottom), new V3(0, 0, Pole.Top), Pole.RadiusAt(Pole.Bottom), Pole.RadiusAt(Pole.Top), Math.Max(Seg, 24)), Lyr.Pole);
            if (D2) si.Add(new FrustumP(new V3(0, 0, Pole.Top), new V3(0, 0, Pole.Top + 25), Pole.RadiusAt(Pole.Top) * 0.9, Pole.RadiusAt(Pole.Top) * 0.75, Seg), Lyr.Pole);
            if (!D0) si.Add(new WireP(new List<V3> { new V3(0, 0, Pole.Bottom - 150), new V3(0, 0, Pole.Top + 200) }, Lyr.Center) { Kind = WireKind.Center });
            si.Anchor = new V3(-R(Pole.Top * 0.45), 0, Pole.Top * 0.45); si.LabelSide = -1;
            S.GroundHalfWidth = Math.Max(3000, Pole.Length * 0.35);
        }

        // ------------------------------------------------------------------ arms & top

        void BuildArm(EquipItem it)
        {
            double z = J.Z(it), a = it.Get("a", 75), len = it.Get("len", 1500), t = it.Get("t", 6);
            bool one = it.Get("oneSided", 0) > 0.5;
            var dir = Dir(it.Side); var tan = Tan(it.Side); double r = R(z);
            var arm = new Arm { Z = z, A = a, Len = len, OneSided = one };
            var si = NewItem(it);
            if (one)
            {
                arm.Axis = dir; arm.Out = dir; arm.Center = At(dir * (r + len / 2), z);
                var p0 = At(dir * r, z); var p1 = At(dir * (r + len), z);
                if (D2) si.Add(new PrismP(At(dir * r, z - a / 2) + tan * (-a / 2), tan, V3.UZ, dir * len, PrismP.Angle(a, t)), Lyr.Equip);
                else si.Add(BoxP.Along(p0, p1, a, a), Lyr.Equip);
                si.Anchor = At(dir * (r + len * 0.7), z + a / 2);
            }
            else
            {
                arm.Axis = tan; arm.Out = dir; arm.Center = At(dir * (r + a / 2), z);
                var c = arm.Center;
                if (D2) si.Add(new PrismP(c - tan * (len / 2) - dir * (a / 2) - V3.UZ * (a / 2), dir, V3.UZ, tan * len, PrismP.Angle(a, t)), Lyr.Equip);
                else si.Add(BoxP.Along(c - tan * (len / 2), c + tan * (len / 2), a, a), Lyr.Equip);
                if (!D0)   // through bolt
                    si.Add(new FrustumP(At(dir * (-r - 30), z), At(dir * (r + a + 30), z), 8, 8, 12), Lyr.Equip);
                si.Anchor = c + tan * (len * 0.45) + V3.UZ * (a / 2);
            }
            _arms[it.Id] = arm;
        }

        void BuildDeadendFit(EquipItem it)
        {
            var arm = FindArmByDef("deadend_arm");
            double z = arm != null ? arm.Z + 150 : J.Z(it);
            var dir = Dir(it.Side); var si = NewItem(it);
            si.Add(BoxP.Along(At(dir * (R(z) - 5), z), At(dir * (R(z) + 170), z), 60, 70), Lyr.Equip);
            Ring(si, z, 50, 6);
            si.Anchor = At(dir * (R(z) + 170), z + 35);
        }

        Arm FindArmByDef(string def)
        {
            var host = J.Items.FirstOrDefault(i => i.On && i.Def == def && _arms.ContainsKey(i.Id));
            return host == null ? null : _arms[host.Id];
        }

        void BuildStrain(EquipItem it)
        {
            var arm = FindArm(it); var si = NewItem(it);
            int per = (int)Math.Max(1, Math.Round(it.Get("perPhase", 2)));
            var line = Tan(0) * 1.0;   // the line arrives from +X
            var fit = On("deadend_fit").FirstOrDefault();
            var starts = new List<V3>();
            if (arm != null && !arm.OneSided)
            {
                double half = arm.Len * 0.42;
                var outFace = arm.Out * (arm.A / 2);
                starts.Add(arm.Center + arm.Axis * (-half) + outFace);
                if (fit != null) { double z = arm.Z + 150; starts.Add(At(Dir(fit.Side) * (R(z) + 170), z)); }
                else starts.Add(arm.Center + outFace);
                starts.Add(arm.Center + arm.Axis * half + outFace);
            }
            else
            {
                double z = arm?.Z ?? J.Z(it);
                for (int k = -1; k <= 1; k++) starts.Add(new V3(R(z) + 100, k * 450, z));
            }
            _strainEnds = new List<V3>();
            double insLen = D0 ? 300 : 180;
            foreach (var s0 in starts)
            {
                var p = s0;
                // shackle / strap
                si.Add(BoxP.Along(p, p + line * 80, 20, 40), Lyr.Equip); p += line * 80;
                for (int k = 0; k < (D0 ? 1 : per); k++) { Insulator(si, p, line, insLen, 75, 3); p += line * insLen; }
                si.Add(BoxP.Along(p, p + line * 140, 40, 50), Lyr.Equip);   // strain clamp
                p += line * 140;
                _strainEnds.Add(p);
            }
            si.Anchor = starts[0] + line * 200 + V3.UZ * 70;
        }

        void BuildService(EquipItem it)
        {
            var si = NewItem(it); double len = it.Get("len", 2500), sag = it.Get("sag", 150);
            var ends = _strainEnds;
            if (ends == null) { double z = J.Z(it); ends = new List<V3>(); for (int k = -1; k <= 1; k++) ends.Add(new V3(R(z) + 300, k * 450, z)); }
            foreach (var e in ends)
            {
                var pts = new List<V3>();
                for (int k = 0; k <= 10; k++) { double t = k / 10.0; pts.Add(e + new V3(len * t, 0, -sag * t * t)); }
                si.Add(new WireP(pts, Lyr.Cable) { Width = 0 });
            }
            si.Anchor = ends[ends.Count - 1] + new V3(len * 0.12, 0, -sag * 0.0144); si.LabelSide = 1;
        }

        // ------------------------------------------------------------------ switchgear

        void BuildPas(EquipItem it)
        {
            double z = J.Z(it), w = it.Get("w", 560), d = it.Get("d", 330), h = it.Get("hgt", 420);
            var dir = Dir(it.Side); var tan = Tan(it.Side); var si = NewItem(it);
            double r = R(z), gap = D0 ? 60 : 130;
            var c = At(dir * (r + gap + d / 2), z);
            si.Add(Box(c, tan, w, d, h), Lyr.Equip);
            if (!D0)
            {
                si.Add(Box(c + V3.UZ * (h / 2 + 20), tan, w + 40, d + 40, 40), Lyr.Equip);   // lid
                si.Add(BoxP.Along(At(dir * (r - 5), z + h * 0.25), At(dir * (r + gap), z + h * 0.25), 80, 80), Lyr.Equip);   // bracket
                si.Add(BoxP.Along(At(dir * (r - 5), z - h * 0.3), At(dir * (r + gap), z - h * 0.3), 60, 60), Lyr.Equip);
                Ring(si, z + h * 0.25, 60, 6); Ring(si, z - h * 0.3, 50, 6);
            }
            if (D2)
            {
                si.Add(Box(c - V3.UZ * (h / 2 + 35) + tan * (w * 0.3), tan, 90, 60, 70), Lyr.Equip);   // operating lever box
                si.Add(new FrustumP(c - V3.UZ * (h / 2) - tan * (w * 0.25), c - V3.UZ * (h / 2 + 90) - tan * (w * 0.25), 22, 22, 12), Lyr.Equip);   // GR / control socket
            }
            _pasPower = new List<V3>(); _pasLoad = new List<V3>();
            int powerSign = tan.X >= -1e-6 ? 1 : -1;
            foreach (int s in new[] { 1, -1 })
                for (int k = -1; k <= 1; k++)
                {
                    var b = c + tan * (s * w / 2) + dir * (k * d * 0.3) + V3.UZ * (h * 0.15);
                    var ax = (tan * (s * Math.Cos(Math.PI / 6)) + V3.UZ * Math.Sin(Math.PI / 6)).Unit;
                    double bl = D0 ? 120 : 170;
                    Insulator(si, b, ax, bl, 40, 3);
                    (s == powerSign ? _pasPower : _pasLoad).Add(b + ax * bl);
                }
            _pasBottom = c - V3.UZ * (h / 2);
            si.Anchor = c + tan * (w / 2) + V3.UZ * (h / 2); si.LabelSide = 1;
            S.Levels.Add((z, "PAS", "PAS"));
        }

        void BuildVct(EquipItem it)
        {
            double w = it.Get("w", 520), d = it.Get("d", 320), h = it.Get("hgt", 420);
            var arm = FindArm(it); var si = NewItem(it);
            V3 c, ax, outv;
            if (arm != null && !arm.OneSided)
            {
                ax = arm.Axis; outv = arm.Out;
                c = arm.Center + outv * (d / 2 - arm.A / 2 + 30) + V3.UZ * (arm.A / 2 + 40 + h / 2);
                if (!D0) foreach (int s in new[] { -1, 1 }) si.Add(Box(c + ax * (s * w * 0.3) - V3.UZ * (h / 2 + 20), ax, 60, d * 0.9, 40), Lyr.Equip);   // feet
            }
            else
            {
                double z = J.Z(it); outv = Dir(it.Side); ax = Tan(it.Side);
                c = At(outv * (R(z) + 120 + d / 2), z);
                if (!D0) si.Add(BoxP.Along(At(outv * R(z), z), At(outv * (R(z) + 120), z), 80, 80), Lyr.Equip);
            }
            si.Add(Box(c, ax, w, d, h), Lyr.Equip);
            if (!D0) si.Add(Box(c + V3.UZ * (h / 2 + 15), ax, w + 30, d + 30, 30), Lyr.Equip);
            _vctIn = new List<V3>(); _vctOut = new List<V3>();
            double top = h / 2 + (D0 ? 0 : 30);
            foreach (int row in new[] { 1, -1 })
                for (int k = -1; k <= 1; k++)
                {
                    var b = c + ax * (k * w * 0.32) + outv * (row * d * 0.22) + V3.UZ * top;
                    Insulator(si, b, V3.UZ, D0 ? 100 : 160, 32, 3);
                    (row > 0 ? _vctIn : _vctOut).Add(b + V3.UZ * (D0 ? 100 : 160));
                }
            // secondary terminal box toward the pole side
            var tb = c - outv * (d / 2 + 50) - V3.UZ * (h * 0.15);
            if (!D0) si.Add(Box(tb, ax, 180, 100, 160), Lyr.Equip);
            _vctTerminal = tb - V3.UZ * 80;
            si.Anchor = c + ax * (w / 2) + V3.UZ * (h / 2); si.LabelSide = 1;
        }

        List<V3> ArmSlots(Arm arm, int n, bool avoidCenter)
        {
            var l = new List<V3>();
            if (arm.OneSided) { for (int k = 0; k < n; k++) l.Add(arm.Top(0.25 + 0.7 * (k + 0.5) / n)); return l; }
            if (!avoidCenter) { for (int k = 0; k < n; k++) l.Add(arm.Top(n == 1 ? 0 : -0.44 + 0.88 * k / (n - 1))); return l; }
            int left = (n + 1) / 2, right = n - left;
            for (int k = 0; k < left; k++) l.Add(arm.Top(left == 1 ? -0.44 : -0.46 + 0.24 * k / (left - 1)));
            for (int k = 0; k < right; k++) l.Add(arm.Top(right == 1 ? 0.44 : 0.22 + 0.24 * k / (right - 1)));
            return l;
        }

        void BuildPins(EquipItem it)
        {
            var arm = FindArm(it); var si = NewItem(it); int n = Math.Max(1, (int)Math.Round(it.Qty));
            _pins = new List<V3>();
            List<V3> bases;
            if (arm != null)
            {
                bool vctHere = J.Items.Any(v => v.On && v.Def == "vct" && FindArm(v) == arm);
                bases = ArmSlots(arm, n, vctHere);
            }
            else { double z = J.Z(it); bases = new List<V3>(); for (int k = 0; k < n; k++) bases.Add(At(Dir(it.Side) * (R(z) + 150) + Tan(it.Side) * ((k - (n - 1) / 2.0) * 300), z)); }
            foreach (var b in bases)
            {
                if (!D0) si.Add(new FrustumP(b, b + V3.UZ * 40, 10, 10, 10), Lyr.Equip);
                Insulator(si, b + V3.UZ * 40, V3.UZ, 110, 60, 2);
                _pins.Add(b + V3.UZ * 150);
            }
            si.Anchor = bases[0] + V3.UZ * 120; si.LabelSide = -1;
        }

        void BuildHeads(EquipItem it)
        {
            var arm = FindArm(it); var si = NewItem(it); int n = Math.Max(1, (int)Math.Round(it.Qty));
            List<V3> tops;
            if (arm != null) tops = ArmSlots(arm, n, false).Select(p => p - V3.UZ * (arm.A + 10)).ToList();
            else { double z = J.Z(it); tops = new List<V3>(); for (int k = 0; k < n; k++) tops.Add(At(Dir(it.Side) * (R(z) + 250 + k * 250), z)); }
            _headTops = new List<V3>(); _headBottoms = new List<V3>();
            foreach (var t in tops)
            {
                var p = t;
                si.Add(BoxP.Along(p, p - V3.UZ * 70, 30, 12), Lyr.Equip); p -= V3.UZ * 70;          // lug
                Insulator(si, p, -V3.UZ, D0 ? 300 : 360, 55, 4); p -= V3.UZ * (D0 ? 300 : 360);
                if (!D0) { si.Add(new FrustumP(p, p - V3.UZ * 160, 28, 22, Seg), Lyr.Equip); p -= V3.UZ * 160; }
                _headTops.Add(t); _headBottoms.Add(p);
            }
            si.Anchor = tops[tops.Count - 1] - V3.UZ * 250; si.LabelSide = 1;
        }

        void BuildLa(EquipItem it)
        {
            var arm = FindArm(it); var si = NewItem(it); int n = Math.Max(1, (int)Math.Round(it.Qty));
            _laTops = new List<V3>();
            List<V3> bases;
            if (arm != null) { var side = V3.UZ.Cross(arm.Axis).Unit * 110; bases = ArmSlots(arm, n, false).Select(p => p + side).ToList(); }
            else { double z = J.Z(it); bases = new List<V3>(); for (int k = 0; k < n; k++) bases.Add(At(Dir(it.Side) * (R(z) + 150) + Tan(it.Side) * ((k - 1) * 220), z)); }
            _laBase = bases[0];
            foreach (var b in bases)
            {
                Insulator(si, b, V3.UZ, 260, 48, 4);
                var top = b + V3.UZ * 260; _laTops.Add(top);
                if (!D0) si.Add(new FrustumP(top, top + V3.UZ * 30, 8, 8, 8), Lyr.Equip);
            }
            if (_headTops != null && !D0)
                for (int k = 0; k < Math.Min(_laTops.Count, _headTops.Count); k++)
                {
                    // lead from the arrester top around the arm edge (not through it) to the cable-head lug
                    var top = _laTops[k] + V3.UZ * 30; var lug = _headTops[k] - V3.UZ * 35;
                    var off = new V3(top.X - lug.X, top.Y - lug.Y, 0); var u = off.Len < 1 ? V3.UY : off.Unit;
                    double armA = arm?.A ?? 75, zArmBot = arm != null ? arm.Z - armA / 2 : lug.Z + 40;
                    var side = new V3(lug.X, lug.Y, 0) + u * (armA / 2 + 70);
                    var pts = new List<V3> { top, top + V3.UZ * 50 + u * 40, At(side, (top.Z + zArmBot) / 2), At(side, zArmBot - 30), At(new V3(lug.X, lug.Y, 0) + u * 20, zArmBot - 30), lug + u * 20 };
                    si.Add(new WireP(pts, Lyr.Cable));
                }
            si.Anchor = bases[0] + V3.UZ * 200; si.LabelSide = 1;
        }

        void BuildBand(EquipItem it)
        {
            var arm = FindArm(it); double z = arm?.Z ?? J.Z(it); var si = NewItem(it);
            Ring(si, z, it.Get("w", 50), 6);
            si.Anchor = new V3(-R(z) - 6, 0, z); si.LabelSide = -1;
        }

        void BuildArmTie(EquipItem it)
        {
            var arm = FindArm(it); if (arm == null || arm.OneSided) { S.Warnings.Add("Arm tie: no two-sided crossarm to mount on."); return; }
            var si = NewItem(it); double drop = it.Get("drop", 450);
            foreach (int s in new[] { -1, 1 })
            {
                var p0 = arm.Bottom(s * 0.33) + V3.UZ * 5;
                double z1 = arm.Z - drop; var p1 = At(arm.Out * (R(z1) + 4), z1);
                si.Add(BoxP.Along(p0, p1, 32, 6, arm.Out), Lyr.Equip);
            }
            si.Anchor = (arm.Bottom(-0.33) + At(arm.Out * R(arm.Z - drop), arm.Z - drop)) * 0.5; si.LabelSide = -1;
        }

        // ------------------------------------------------------------------ boxes

        void BuildBox(EquipItem it)
        {
            double z = J.Z(it), w = it.Get("w", 300), d = it.Get("d", 200), h = it.Get("hgt", 400);
            var dir = Dir(it.Side); var tan = Tan(it.Side); var si = NewItem(it);
            double gap = D0 ? 20 : 45; var c = At(dir * (R(z) + gap + d / 2), z);
            si.Add(Box(c, tan, w, d, h), Lyr.Equip);
            if (it.Get("roof", 0) > 0.5 && !D0)
            {
                var prof = new List<P2> { new P2(-d / 2 - 15, 0), new P2(d / 2 + 25, 0), new P2(d / 2 + 25, 15), new P2(-d / 2 - 15, 45) };
                si.Add(new PrismP(c + V3.UZ * (h / 2) - tan * (w / 2 + 12), dir, V3.UZ, tan * (w + 24), prof), Lyr.Equip);
            }
            if (!D0)
            {
                si.Add(BoxP.Along(At(dir * (R(z + h * 0.3) - 5), z + h * 0.3), At(dir * (R(z) + gap), z + h * 0.3), 50, 40), Lyr.Equip);
                si.Add(BoxP.Along(At(dir * (R(z - h * 0.3) - 5), z - h * 0.3), At(dir * (R(z) + gap), z - h * 0.3), 50, 40), Lyr.Equip);
                Ring(si, z + h * 0.3, 40, 6); Ring(si, z - h * 0.3, 40, 6);
            }
            if (D2)
            {
                si.Add(Box(c + dir * (d / 2 + 3), tan, w - 36, 6, h - 36), Lyr.Equip);   // door
                si.Add(Box(c + dir * (d / 2 + 12) + tan * (w / 2 - 45), tan, 14, 14, 50), Lyr.Equip);   // handle
            }
            _boxes[it.Id] = (c, dir, w, d, h);
            si.Anchor = c + dir * (d / 2) + V3.UZ * (h * 0.3);
            si.LabelSide = Math.Abs(dir.X) < 0.3 ? -1 : Math.Sign(dir.X);
        }

        void BuildCyl(EquipItem it)
        {
            double z = J.Z(it), dia = it.Get("dia", 200), h = it.Get("hgt", 400);
            var dir = Dir(it.Side); var si = NewItem(it);
            var c = At(dir * (R(z) + 40 + dia / 2), z);
            si.Add(new FrustumP(c - V3.UZ * (h / 2), c + V3.UZ * (h / 2), dia / 2, dia / 2, Seg), Lyr.Equip);
            if (!D0) { si.Add(BoxP.Along(At(dir * (R(z) - 5), z), At(dir * (R(z) + 40), z), 50, 40), Lyr.Equip); Ring(si, z, 40, 6); }
            _boxes[it.Id] = (c, dir, dia, dia, h);
            si.Anchor = c + dir * (dia / 2); si.LabelSide = Math.Abs(dir.X) < 0.3 ? -1 : Math.Sign(dir.X);
        }

        // ------------------------------------------------------------------ pipes

        V3 PipeAxis(double side, double dia, double z) => Dir(side) * (R(Math.Min(z, 0)) + dia / 2 + 12);

        /// <summary>Quarter bend from vertical (going down) to horizontal toward 'outDir', returns the end point.</summary>
        V3 Bend(SceneItem si, V3 start, V3 outDir, double rad, double radius, Lyr l)
        {
            var c = start + outDir * radius;   // bend centre
            V3 prev = start; int n = D0 ? 3 : 6;
            for (int k = 1; k <= n; k++)
            {
                double a = Math.PI / 2 * k / n;
                var p = c - outDir * (radius * Math.Cos(a)) - V3.UZ * (radius * Math.Sin(a));
                si.Add(new FrustumP(prev, p, rad, rad, Seg), l); prev = p;
            }
            return prev;
        }

        void BuildRiser(EquipItem it, EquipItem hv)
        {
            double dia = it.Get("dia", 113.4); double top = J.Z(it);
            double side = hv?.Side ?? it.Side;
            var axis = PipeAxis(side, dia, 0); var dir = Dir(side); var si = NewItem(it);
            double bottom = -(_ugDepth - _bendR);
            si.Add(new FrustumP(At(axis, bottom), At(axis, top), dia / 2, dia / 2, Seg), Lyr.Equip);
            if (!D0)
            {   // entrance cap
                si.Add(new FrustumP(At(axis, top), At(axis, top + 90), dia * 0.6, dia * 0.55, Seg), Lyr.Equip);
                var p = At(axis, top + 90);
                si.Add(new FrustumP(p, p + (dir + V3.UZ).Unit * 110, dia * 0.42, dia * 0.42, Seg), Lyr.Equip);
            }
            var end = Bend(si, At(axis, bottom), new V3(dir.X, dir.Y, 0).Unit, dia / 2, _bendR, Lyr.Equip);
            _ugStart = end; _ugDir = new V3(dir.X, dir.Y, 0).Unit;
            _hasRiser = true; _riserAxis = axis; _riserTop = top; _riserDia = dia;
            _pipes.Add((axis, top, 0, dia));
            si.Anchor = At(axis + dir * (dia / 2), top * 0.55); si.LabelSide = 1;
        }

        void BuildConduit(EquipItem it)
        {
            double dia = it.Get("dia", 34), top = it.Get("top", 2400), bottom = it.Get("bottom", 0);
            var host = J.Items.FirstOrDefault(h => h.On && h.Def == Catalog.Find(it.Def)?.AtDef && _boxes.ContainsKey(h.Id));
            if (host != null) { var b = _boxes[host.Id]; top = b.c.Z - b.h / 2; }
            var axis = PipeAxis(it.Side, dia, 0); var dir = Dir(it.Side); var si = NewItem(it);
            double depth = 600, rb = Math.Max(200, dia * 4);
            si.Add(new FrustumP(At(axis, -(depth - rb)), At(axis, top), dia / 2, dia / 2, Seg), Lyr.Equip);
            if (bottom <= 0)
            {
                var end = Bend(si, At(axis, -(depth - rb)), dir, dia / 2, rb, Lyr.Equip);
                si.Add(new FrustumP(end, end + dir * 400, dia / 2, dia / 2, Seg), Lyr.Equip);
            }
            _pipes.Add((axis, top, 0, dia));
            si.Anchor = At(axis + dir * (dia / 2), Math.Min(top - 300, top * 0.75)); si.LabelSide = dir.X < -0.1 ? -1 : 1;
        }

        void BuildPipeBands(EquipItem it)
        {
            if (_pipes.Count == 0 || D0) return;
            var si = NewItem(it); double pitch = Math.Max(300, it.Get("pitch", 1500)); int count = 0;
            foreach (var (axis, top, bottom, dia) in _pipes)
            {
                for (double z = 400; z < top - 150; z += pitch)
                {
                    var h = new V3(axis.X, axis.Y, 0); var u = h.Unit; double r = R(z);
                    // strap: from the pole surface around the pipe
                    si.Add(BoxP.Along(At(u * (r - 4), z), At(u * (h.Len + dia / 2 + 6), z), dia + 12, 30), Lyr.Equip);
                    count++;
                }
                foreach (var z in new[] { top - 120 })
                {
                    var h = new V3(axis.X, axis.Y, 0); var u = h.Unit;
                    si.Add(BoxP.Along(At(u * (R(z) - 4), z), At(u * (h.Len + dia / 2 + 6), z), dia + 12, 30), Lyr.Equip); count++;
                }
            }
            si.Qty = count;
            var a0 = _pipes[0]; si.Anchor = At(a0.axis.Unit * (a0.axis.Len + a0.dia / 2 + 6), 1900); si.LabelSide = 1;
        }

        // ------------------------------------------------------------------ cables

        void BuildJumpers(EquipItem it)
        {
            var si = NewItem(it); var conn = FirstDef("bolt_conn");
            SceneItem ci = conn != null ? NewItem(conn) : null;
            // stages from the line to the cable heads
            var stages = new List<(List<V3> a, List<V3> b)>();
            List<V3> prev = _strainEnds;
            void Link(List<V3> into, List<V3> outOf) { if (into != null && into.Count > 0) { if (prev != null) stages.Add((prev, into)); prev = outOf ?? into; } }
            if (_pasPower != null) Link(_pasPower, _pasLoad);
            if (_pins != null && _pins.Count >= 3 && (_vctIn != null || _headTops != null)) Link(_pins, _pins);
            if (_vctIn != null) Link(_vctIn, _vctOut);
            if (_headTops != null) Link(_headTops, null);
            int stageNo = 0;
            foreach (var (a, b) in stages)
            {
                var sa = Sorted(a); var sb = Sorted(b); int n = Math.Min(sa.Count, sb.Count);
                for (int k = 0; k < n; k++)
                {
                    var pts = Jumper(sa[k], sb[k], stageNo == 0 ? 0.08 : 0.15);
                    si.Add(new WireP(pts, Lyr.Cable));
                    if (ci != null && stageNo > 0)
                    {
                        var m = pts[pts.Count / 2];
                        ci.Add(BoxP.Along(m - V3.UZ * 45, m + V3.UZ * 45, 55, 40), Lyr.Equip);
                        if (ci.Anchor == null) { ci.Anchor = m; ci.LabelSide = -1; }
                    }
                }
                stageNo++;
            }
            if (stages.Count == 0) S.Warnings.Add("HV jumpers: nothing to connect (add PAS / VCT / cable heads).");
            var any = si.Prims.OfType<WireP>().FirstOrDefault();
            if (any != null) { si.Anchor = any.Pts[any.Pts.Count / 2]; si.LabelSide = -1; }
        }

        static List<V3> Sorted(List<V3> l) => l.OrderBy(p => p.Y * 1000 + p.X).ToList();

        void BuildHvCable(EquipItem it)
        {
            var si = NewItem(it); var dir = Dir(it.Side);
            var axis = _hasRiser ? _riserAxis : PipeAxis(it.Side, 60, 0);
            double topZ = _headBottoms != null && _headBottoms.Count > 0 ? _headBottoms.Min(p => p.Z) - 450 : (_pasBottom?.Z ?? Pole.Top - 1500) - 600;
            var tri = At(axis, topZ);
            if (_headBottoms != null)
                foreach (var hb in _headBottoms) si.Add(new WireP(Bezier(hb, new V3(hb.X, hb.Y, (hb.Z + topZ) / 2), tri, 10), Lyr.Cable));
            else if (_pasLoad != null)
                foreach (var p in _pasLoad) si.Add(new WireP(Jumper(p, tri), Lyr.Cable));
            if (!D0 && _headBottoms != null)   // trifurcation sleeve
                si.Add(new FrustumP(tri + V3.UZ * 60, tri - V3.UZ * 120, 40, 32, Seg), Lyr.Equip);
            var path = new List<V3> { tri };
            double bottom = -(_ugDepth - _bendR);
            path.Add(At(axis, bottom));
            var outDir = _hasRiser ? _ugDir : new V3(dir.X, dir.Y, 0).Unit;
            var c = At(axis, bottom) + outDir * _bendR;
            for (int k = 1; k <= 8; k++) { double a = Math.PI / 2 * k / 8; path.Add(c - outDir * (_bendR * Math.Cos(a)) - V3.UZ * (_bendR * Math.Sin(a))); }
            double toX = it.Get("toX", 6000);
            var hh = On("handhole").FirstOrDefault();
            if (hh != null) toX = hh.Get("dist", 3500);
            var last = path[path.Count - 1];
            path.Add(new V3(outDir.X * toX, outDir.Y * toX, last.Z));
            si.Add(new WireP(path, Lyr.Cable));
            si.Anchor = At(axis, Math.Max(topZ - 500, topZ * 0.8)); si.LabelSide = 1;
        }

        /// <summary>
        /// Cable run down (or up) the pole surface. pass: 0 = behind boxes (between box and pole, default),
        /// 1 = in front of boxes (the cable goes round the front face). 2 = beside: handled by the arranging logic.
        /// 'skip' = boxes the cable starts / ends at (no detour around those).
        /// </summary>
        void PoleRun(List<V3> pts, double side, double z0, double z1, double clear, int pass = 0, ICollection<string> skip = null)
        {
            var d = Dir(side);
            if (Math.Abs(z1 - z0) < 1) return;
            // radial distance of the cable at height z (in front of a box when asked)
            var fronts = new List<(double lo, double hi, double r)>();
            if (pass == 1)
                foreach (var kv in _boxes)
                {
                    if (skip != null && skip.Contains(kv.Key)) continue;
                    var b = kv.Value; double ang = Math.Atan2(b.dir.X, -b.dir.Y) * 180 / Math.PI;
                    double rb = R(b.c.Z) + 45;   // box back face
                    double half = Math.Atan2(b.w / 2 + 30, rb) * 180 / Math.PI;
                    double diff = Math.Abs(((side - ang) % 360 + 540) % 360 - 180);
                    if (diff > half) continue;
                    var hz = new V3(b.c.X, b.c.Y, 0).Len;   // distance of the box centre from the pole axis
                    fronts.Add((b.c.Z - b.h / 2 - 60, b.c.Z + b.h / 2 + 60, (hz + b.d / 2 + 25) / Math.Max(0.3, Math.Cos(diff * Math.PI / 180))));
                }
            double Rad(double z) { double r = R(z) + clear; foreach (var f in fronts) if (z >= f.lo && z <= f.hi) r = Math.Max(r, f.r); return r; }
            var zs = new List<double> { z0, z1 };
            for (double z = Math.Min(z0, z1) + 1000; z < Math.Max(z0, z1); z += 1000) zs.Add(z);
            foreach (var f in fronts)
                foreach (var z in new[] { f.lo - 120, f.lo, f.hi, f.hi + 120 })
                    if (z > Math.Min(z0, z1) && z < Math.Max(z0, z1)) zs.Add(z);
            zs = (z1 < z0 ? zs.OrderByDescending(z => z) : zs.OrderBy(z => z)).Distinct().ToList();
            foreach (var z in zs) pts.Add(At(d * Rad(z), z));
        }

        void BuildCtrl(EquipItem it)
        {
            if (_pasBottom == null) { S.Warnings.Add("PAS control cable: no PAS."); return; }
            var si = NewItem(it); var host = FirstDef("sog_box");
            double side = it.Get("fixedSide", 0) > 0.5 ? it.Side : (FirstDef("pas")?.Side ?? 0) - 20;
            var pts = new List<V3> { _pasBottom.Value };
            double zEnd = 0;
            if (host != null && _boxes.TryGetValue(host.Id, out var b)) zEnd = b.c.Z + b.h / 2;
            PoleRun(pts, side, _pasBottom.Value.Z - 150, zEnd, 25, (int)it.Get("pass", 0), host == null ? null : new[] { host.Id });
            if (host != null && _boxes.TryGetValue(host.Id, out var bb)) pts.Add(bb.c + V3.UZ * (bb.h / 2));
            si.Add(new WireP(pts, Lyr.Cable) { Kind = WireKind.Solid });
            si.Anchor = pts[Math.Min(2, pts.Count - 1)]; si.LabelSide = -1;
        }

        void BuildMeterCable(EquipItem it)
        {
            if (_vctTerminal == null) { S.Warnings.Add("Meter cable: no VCT."); return; }
            var si = NewItem(it); var host = FirstDef("meter_box");
            double side = it.Get("fixedSide", 0) > 0.5 ? it.Side : (host?.Side ?? 90) - 25;
            var pts = new List<V3> { _vctTerminal.Value };
            double zEnd = 0;
            if (host != null && _boxes.TryGetValue(host.Id, out var b)) zEnd = b.c.Z + b.h / 2;
            PoleRun(pts, side, _vctTerminal.Value.Z - 200, zEnd, 25, (int)it.Get("pass", 0), host == null ? null : new[] { host.Id });
            if (host != null && _boxes.TryGetValue(host.Id, out var bb)) pts.Add(bb.c + V3.UZ * (bb.h / 2));
            si.Add(new WireP(pts, Lyr.Cable) { Kind = WireKind.Hidden });
            si.Anchor = pts[Math.Min(2, pts.Count - 1)]; si.LabelSide = 1;
        }

        /// <summary>
        /// Signal cable between two boxes (default: meter box -> pulse converter box), then down the box's conduit
        /// (or along the pole) to GL. P "fromDef" not needed: the source is the meter box, the target the item's host.
        /// </summary>
        void BuildLinkCable(EquipItem it)
        {
            var hostDef = Catalog.Find(it.Def)?.AtDef ?? "pulse_box";
            var to = J.Items.FirstOrDefault(i => i.On && (i.Id == it.At || (string.IsNullOrEmpty(it.At) && i.Def == hostDef)) && _boxes.ContainsKey(i.Id));
            if (to == null) { S.Warnings.Add("Pulse signal cable: no pulse converter box."); return; }
            var from = J.Items.FirstOrDefault(i => i.On && i.Def == "meter_box" && _boxes.ContainsKey(i.Id));
            var b = _boxes[to.Id];
            var si = NewItem(it); var pts = new List<V3>();
            var bBot = b.c - V3.UZ * (b.h / 2);
            if (from != null)
            {
                var a = _boxes[from.Id]; var aBot = a.c - V3.UZ * (a.h / 2);
                double z = Math.Min(aBot.Z, bBot.Z) - 80;
                pts.Add(aBot); pts.Add(new V3(aBot.X, aBot.Y, z));
                pts.Add(new V3(bBot.X, bBot.Y, z)); pts.Add(bBot);
            }
            else pts.Add(bBot);
            // down the conduit of the pulse box (if any), else along the pole surface
            var cond = J.Items.FirstOrDefault(i => i.On && Catalog.Find(i.Def)?.Template == "conduit" && (i.At == to.Id || Catalog.Find(i.Def)?.AtDef == to.Def));
            if (cond != null)
            {
                var axis = PipeAxis(cond.Side, cond.Get("dia", 26.5), 0);
                pts.Add(At(axis, bBot.Z - 40)); pts.Add(At(axis, 0)); pts.Add(At(axis, -300));
            }
            else
            {
                var dirB = new V3(b.c.X, b.c.Y, 0).Unit; double ang = Math.Atan2(dirB.X, -dirB.Y) * 180 / Math.PI;
                PoleRun(pts, ang, bBot.Z - 100, 0, 20);
            }
            si.Add(new WireP(pts, Lyr.Cable) { Kind = WireKind.Hidden });
            si.Anchor = pts[Math.Min(pts.Count - 1, from != null ? 4 : 1)]; si.LabelSide = b.dir.X < 0 ? -1 : 1;
        }

        // ------------------------------------------------------------------ guy

        void BuildGuy(EquipItem it)
        {
            double zA = J.Z(it), dist = it.Get("dist", 5000), insH = it.Get("insH", 4800), guard = it.Get("guard", 2000);
            var dir = Dir(it.Side); var si = NewItem(it);
            var A = At(dir * (R(zA) + 15), zA); var G = At(dir * dist, 0);
            var u = (G - A).Unit;
            Ring(si, zA, 60, 6);
            si.Add(new WireP(new List<V3> { A, G }, Lyr.Guy));
            si.Anchor = V3.Lerp(A, G, 0.3); si.LabelSide = -1;
            SceneItem Part(string def) { var p = FirstDef(def); return p == null ? si : NewItem(p); }
            // insulator
            if (insH > 0 && insH < zA)
            {
                var pi = Part("guy_ins"); double t = (zA - insH) / zA; var I = V3.Lerp(A, G, t);
                pi.Add(new FrustumP(I - u * 60, I, 15, 48, Seg), Lyr.Guy); pi.Add(new FrustumP(I, I + u * 60, 48, 15, Seg), Lyr.Guy);
                if (pi != si) { pi.Anchor = I; pi.LabelSide = -1; }
                if (D2)
                {
                    var gp = Part("guy_grip");
                    gp.Add(new FrustumP(I - u * 420, I - u * 60, 7, 7, 8), Lyr.Guy); gp.Add(new FrustumP(I + u * 60, I + u * 420, 7, 7, 8), Lyr.Guy);
                    if (gp != si) { gp.Anchor = I + u * 300; gp.LabelSide = -1; }
                }
            }
            if (D2)   // つるまきストッパー
            {
                var s0 = V3.Lerp(A, G, 0.62); si.Add(new FrustumP(s0, s0 + u * 650, 28, 28, Seg), Lyr.Guy);
            }
            if (guard > 0)
            {
                var pg = Part("guy_guard");
                pg.Add(new FrustumP(G - u * guard, G, 22, 22, Seg), Lyr.Guy);
                if (pg != si) { pg.Anchor = G - u * (guard * 0.6); pg.LabelSide = -1; }
            }
            // rod + anchor block
            var pa = Part("guy_anchor");
            var K = G + u * 1700;
            pa.Add(new FrustumP(G - u * 250, K, 11, 11, 10), Lyr.Guy);
            var ax = V3.UZ.Cross(new V3(u.X, u.Y, 0)).Unit; var ay = u.Cross(ax);
            pa.Add(new BoxP(K + u * 75, 550, 400, 150) { Ax = ax, Ay = ay, Az = u }, Lyr.Guy);
            if (pa != si) { pa.Anchor = K; pa.LabelSide = -1; }
        }

        // ------------------------------------------------------------------ earthing

        readonly List<V3> _rodTops = new List<V3>();

        void BuildEarthRods(EquipItem it)
        {
            var si = NewItem(it); int n = Math.Max(1, (int)Math.Round(it.Qty)); double zt = J.Z(it); var dir = Dir(it.Side);
            for (int k = 0; k < n; k++)
            {
                var top = At(dir * (R(0) + 800 + k * 1800), zt);
                si.Add(new FrustumP(top, top - V3.UZ * 1500, 9, 9, 10), Lyr.Earth);
                if (!D0) si.Add(new BoxP(top + V3.UZ * 25, 60, 40, 50), Lyr.Earth);
                _rodTops.Add(top);
            }
            si.Anchor = _rodTops[0] - V3.UZ * 600; si.LabelSide = dir.X < 0 ? -1 : 1;
        }

        void BuildEarth(EquipItem it)
        {
            int from = (int)Math.Round(it.Get("from", 0));
            V3? src = null;
            if (from == 2) src = _laBase ?? _rodBase ?? _pasBottom;
            if (from == 0) src = _pasBottom ?? _laBase;
            if (src == null)
            {
                var arm = _arms.Values.OrderByDescending(a => a.Z).FirstOrDefault();
                if (arm != null) src = arm.Center - arm.Out * (arm.A / 2) - V3.UZ * (arm.A / 2);
            }
            if (src == null) { S.Warnings.Add("Earth wire: nothing to earth."); return; }
            var si = NewItem(it); var pts = new List<V3> { src.Value };
            PoleRun(pts, it.Side, src.Value.Z - 200, 0, 12, (int)it.Get("pass", 0));
            V3 rod = _rodTops.Count == 0 ? At(Dir(it.Side) * (R(0) + 800), -750) : _rodTops[Math.Min(from, _rodTops.Count - 1)];
            var g = pts[pts.Count - 1];
            pts.Add(new V3(g.X, g.Y, rod.Z)); pts.Add(rod + V3.UZ * 50);
            si.Add(new WireP(pts, Lyr.Earth) { Kind = from == 0 ? WireKind.Solid : WireKind.Hidden });
            si.Anchor = pts[Math.Min(3, pts.Count - 1)]; si.LabelSide = Dir(it.Side).X < 0 ? -1 : 1;
        }

        // ------------------------------------------------------------------ foundation & underground

        void BuildNeka(EquipItem it)
        {
            var si = NewItem(it); double z = J.Z(it); int n = Math.Max(1, (int)Math.Round(it.Qty));
            for (int k = 0; k < n; k++)
            {
                double zk = z - k * 300; double side = it.Side + 90 * k; var dir = Dir(side); var tan = Tan(side);
                var c = At(dir * (R(zk) + 85), zk);
                si.Add(Box(c, tan, 1000, 170, 140), Lyr.Pole);
                if (!D0) si.Add(new FrustumP(c - dir * 85 + V3.UZ * 40 - tan * 60, c - dir * 85 + V3.UZ * 40 + tan * 60, 8, 8, 8), Lyr.Pole);
            }
            si.Anchor = At(Dir(it.Side) * (R(z) + 170), z) + Tan(it.Side) * (-450); si.LabelSide = -1;
        }

        void BuildBasePlate(EquipItem it)
        {
            var si = NewItem(it); double z = Pole.Bottom;
            si.Add(new BoxP(new V3(0, 0, z - 60), 600, 600, 120), Lyr.Pole);
            si.Anchor = new V3(300, 0, z - 60); si.LabelSide = 1;
        }

        void BuildUgPipe(EquipItem it)
        {
            var si = NewItem(it); int n = Math.Max(1, (int)Math.Round(it.Get("n", 4))); double dia = it.Get("dia", 125), dia2 = it.Get("dia2", 40);
            double len = it.Get("len", 3000);
            var dir = _hasRiser ? _ugDir : new V3(Dir(it.Side).X, Dir(it.Side).Y, 0).Unit;
            V3 start = _ugStart ?? At(dir * (R(0) + 300), J.Z(it));
            var hh = On("handhole").FirstOrDefault();
            if (hh != null) { double d = hh.Get("dist", 3500) - hh.Get("w", 600) / 2; len = Math.Max(300, d - new V3(start.X, start.Y, 0).Dot(dir)); }
            double z = start.Z;
            for (int k = 0; k < n; k++)
            {
                double dk = k == 0 ? dia : dia2;
                var p0 = new V3(start.X, start.Y, z);
                si.Add(new FrustumP(p0, p0 + dir * len, dk / 2, dk / 2, Seg), Lyr.Equip);
                if (k + 1 < n) z -= dk / 2 + 30 + dia2 / 2;
            }
            si.Anchor = start + dir * (len * 0.6) - V3.UZ * (dia / 2); si.LabelSide = 1;
        }

        void BuildHandhole(EquipItem it)
        {
            var si = NewItem(it); double dist = it.Get("dist", 3500), w = it.Get("w", 600), h = it.Get("hgt", 600);
            var dir = new V3(Dir(it.Side).X, Dir(it.Side).Y, 0).Unit;
            var c = At(dir * dist, 60 - (h + 200) / 2);
            si.Add(Box(c, Tan(it.Side), w + 200, w + 200, h + 200), Lyr.Pole);
            if (!D0) si.Add(Box(At(dir * dist, 70), Tan(it.Side), w + 160, w + 160, 20), Lyr.Pole);   // lid
            si.Anchor = c + V3.UZ * ((h + 200) / 2); si.LabelSide = 1;
        }

        void BuildLightningRod(EquipItem it)
        {
            var si = NewItem(it); double len = it.Get("len", 1000), z = Pole.Top + 25;
            var dir = Dir(it.Side); double off = it.Side == 0 && it.Ref == HRef.Top && it.H == 0 ? 0 : R(Pole.Top) + 60;
            var b = At(dir * off, Pole.Top - 400);
            if (off > 0)   // clamped to the pole side with two bands
            {
                si.Add(new FrustumP(b, b + V3.UZ * (len + 400), 12, 9, 10), Lyr.Equip);
                Ring(si, Pole.Top - 300, 40, 6); Ring(si, Pole.Top - 100, 40, 6);
                si.Add(new FrustumP(b + V3.UZ * (len + 400), b + V3.UZ * (len + 480), 9, 1, 10), Lyr.Equip);
                _rodBase = b;
            }
            else
            {
                si.Add(new FrustumP(new V3(0, 0, z), new V3(0, 0, z + 60), R(Pole.Top) * 0.9, 25, Seg), Lyr.Equip);
                si.Add(new FrustumP(new V3(0, 0, z + 60), new V3(0, 0, z + 60 + len), 12, 9, 10), Lyr.Equip);
                si.Add(new FrustumP(new V3(0, 0, z + 60 + len), new V3(0, 0, z + 140 + len), 9, 1, 10), Lyr.Equip);
                _rodBase = new V3(R(Pole.Top), 0, Pole.Top - 100);
            }
            S.Levels.Add((Pole.Top + 60 + len, "避雷針", "Lightning rod"));
            si.Anchor = off > 0 ? b + V3.UZ * (len * 0.8 + 400) : new V3(0, 0, z + 60 + len * 0.7); si.LabelSide = -1;
        }

        void BuildSteps(EquipItem it)
        {
            if (D0) return;
            var si = NewItem(it); double pitch = Math.Max(150, it.Get("pitch", 450)), topGap = it.Get("topGap", 500);
            int k = 0;
            for (double z = J.Z(it); z <= Pole.Top - topGap; z += pitch, k++)
            {
                var dir = Dir(it.Side + (k % 2 == 0 ? 90 : 270)); double r = R(z);
                var p0 = At(dir * (r - 15), z); var p1 = At(dir * (r + 190), z);
                si.Add(new FrustumP(p0, p1, 8, 8, 8), Lyr.Equip);
                if (D2) si.Add(new FrustumP(p1, p1 + V3.UZ * 40, 8, 8, 8), Lyr.Equip);
            }
            si.Qty = k;
            si.Anchor = At(Dir(it.Side + 270) * (R(J.Z(it) + pitch) + 190), J.Z(it) + pitch); si.LabelSide = -1;
            if (k == 0) S.Items.Remove(si);
        }
    }
}
