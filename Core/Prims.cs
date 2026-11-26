using System;
using System.Collections.Generic;

namespace PoleToolApp.Core
{
    /// <summary>Layers of generated geometry (the CAD layer names get a user prefix).</summary>
    public enum Lyr { Pole, Equip, Cable, Earth, Guy, Ground, Text, Dim, Bom, Hidden, Center }

    /// <summary>Closed triangle/polygon mesh. Faces are counter-clockwise seen from outside.</summary>
    public sealed class Mesh
    {
        public readonly List<V3> V = new List<V3>();
        public readonly List<int[]> F = new List<int[]>();
        /// <summary>Faces that only close a slice of a longer solid: their edges are never drawn.</summary>
        public readonly HashSet<int> Virtual = new HashSet<int>();
        public int Add(V3 p) { V.Add(p); return V.Count - 1; }
        public void Face(params int[] idx) => F.Add(idx);
        public V3 Normal(int[] f)
        {
            // Newell's method, robust for polygons
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < f.Length; i++)
            {
                var a = V[f[i]]; var b = V[f[(i + 1) % f.Length]];
                x += (a.Y - b.Y) * (a.Z + b.Z); y += (a.Z - b.Z) * (a.X + b.X); z += (a.X - b.X) * (a.Y + b.Y);
            }
            return new V3(x, y, z).Unit;
        }
        public V3 Center { get { V3 s = V3.Zero; foreach (var p in V) s += p; return V.Count == 0 ? s : s * (1.0 / V.Count); } }
    }

    public abstract class Prim
    {
        public Lyr Layer = Lyr.Equip;
        /// <summary>AutoCAD colour index, 0 = ByBlock / layer colour.</summary>
        public int Color;
        public abstract Mesh ToMesh();
    }

    /// <summary>Box with centre and three (orthonormal) axes.</summary>
    public sealed class BoxP : Prim
    {
        public V3 C, Ax = V3.UX, Ay = V3.UY, Az = V3.UZ; public double Sx, Sy, Sz;
        /// <summary>Bit i set = face i is a cut of a sliced box (edges not drawn). Faces: 0 −Z, 1 +Z, 2 −Y, 3 +Y, 4 −X, 5 +X.</summary>
        public int Cut;
        public BoxP(V3 c, double sx, double sy, double sz) { C = c; Sx = sx; Sy = sy; Sz = sz; }
        /// <summary>Box whose long axis runs from a to b (cross-section w × h, h along 'up').</summary>
        public static BoxP Along(V3 a, V3 b, double w, double h, V3? up = null)
        {
            var ax = (b - a).Unit; var u = up ?? V3.UZ;
            if (Math.Abs(ax.Dot(u)) > 0.95) u = V3.UY;
            var ay = u.Cross(ax).Unit; var az = ax.Cross(ay).Unit;
            return new BoxP((a + b) * 0.5, (b - a).Len, w, h) { Ax = ax, Ay = ay, Az = az };
        }
        public override Mesh ToMesh()
        {
            var m = new Mesh();
            for (int i = 0; i < 8; i++)
            {
                double sx = (i & 1) == 0 ? -0.5 : 0.5, sy = (i & 2) == 0 ? -0.5 : 0.5, sz = (i & 4) == 0 ? -0.5 : 0.5;
                m.Add(C + Ax * (sx * Sx) + Ay * (sy * Sy) + Az * (sz * Sz));
            }
            m.Face(0, 2, 3, 1); m.Face(4, 5, 7, 6);   // -z, +z
            m.Face(0, 1, 5, 4); m.Face(2, 6, 7, 3);   // -y, +y
            m.Face(0, 4, 6, 2); m.Face(1, 3, 7, 5);   // -x, +x
            for (int i = 0; i < 6; i++) if ((Cut >> i & 1) != 0) m.Virtual.Add(i);
            return m;
        }
    }

    /// <summary>Cone frustum / cylinder between two points (r1 at a, r2 at b).</summary>
    public sealed class FrustumP : Prim
    {
        public V3 A, B; public double R1, R2; public int Seg = 24;
        /// <summary>False = the end face is a cut of a sliced solid (edges not drawn).</summary>
        public bool CapA = true, CapB = true;
        public FrustumP(V3 a, V3 b, double r1, double r2, int seg = 24) { A = a; B = b; R1 = Math.Max(r1, 0.01); R2 = Math.Max(r2, 0.01); Seg = Math.Max(6, seg); }
        public override Mesh ToMesh()
        {
            var m = new Mesh(); var axis = B - A; axis.Basis(out var u, out var v);
            for (int i = 0; i < Seg; i++)
            {
                double t = 2 * Math.PI * i / Seg; var d = u * Math.Cos(t) + v * Math.Sin(t);
                m.Add(A + d * R1); m.Add(B + d * R2);
            }
            var bot = new int[Seg]; var top = new int[Seg];
            for (int i = 0; i < Seg; i++)
            {
                int j = (i + 1) % Seg;
                m.Face(2 * i, 2 * j, 2 * j + 1, 2 * i + 1);
                bot[i] = 2 * (Seg - 1 - i); top[i] = 2 * i + 1;
            }
            m.Face(bot); m.Face(top);
            if (!CapA) m.Virtual.Add(m.F.Count - 2);
            if (!CapB) m.Virtual.Add(m.F.Count - 1);
            // orientation check: side normal must point away from the axis
            var n = m.Normal(m.F[0]); var mid = (m.V[0] + m.V[1]) * 0.5; var c = (A + B) * 0.5;
            if (n.Dot(mid - c) < 0) for (int k = 0; k < m.F.Count; k++) Array.Reverse(m.F[k]);
            return m;
        }
    }

    /// <summary>Planar profile (in the plane O + s·U + t·V) extruded along E.</summary>
    public sealed class PrismP : Prim
    {
        public V3 O, U, V, E; public List<P2> Profile;
        /// <summary>False = the start / end face is a cut of a sliced prism (edges not drawn).</summary>
        public bool CapB = true, CapT = true;
        public PrismP(V3 o, V3 u, V3 v, V3 e, List<P2> profile) { O = o; U = u.Unit; V = v.Unit; E = e; Profile = profile; }
        public override Mesh ToMesh()
        {
            var m = new Mesh(); int n = Profile.Count;
            foreach (var p in Profile) { var q = O + U * p.X + V * p.Y; m.Add(q); m.Add(q + E); }
            var b = new int[n]; var t = new int[n];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n; m.Face(2 * i, 2 * j, 2 * j + 1, 2 * i + 1);
                b[i] = 2 * (n - 1 - i); t[i] = 2 * i + 1;
            }
            m.Face(b); m.Face(t);
            if (!CapB) m.Virtual.Add(m.F.Count - 2);
            if (!CapT) m.Virtual.Add(m.F.Count - 1);
            // make the profile orientation agree with E (signed area × (U×V)·E)
            double s = 0; for (int i = 0; i < n; i++) { var a = Profile[i]; var c = Profile[(i + 1) % n]; s += a.X * c.Y - c.X * a.Y; }
            if (s * U.Cross(V).Dot(E) < 0) for (int k = 0; k < m.F.Count; k++) Array.Reverse(m.F[k]);
            return m;
        }
        public static List<P2> Rect(double w, double h) => new List<P2> { new P2(-w / 2, -h / 2), new P2(w / 2, -h / 2), new P2(w / 2, h / 2), new P2(-w / 2, h / 2) };
        /// <summary>Equal-leg angle (L-profile) a × a × t, corner at 0,0.</summary>
        public static List<P2> Angle(double a, double t) => new List<P2> { new P2(0, 0), new P2(a, 0), new P2(a, t), new P2(t, t), new P2(t, a), new P2(0, a) };
    }

    public enum WireKind { Solid, Hidden, Center, Phantom }

    /// <summary>Cable / wire / pipe centre line. Not a solid: drawn as a polyline (thickness = drawing width, 0 = thin).</summary>
    public sealed class WireP : Prim
    {
        public List<V3> Pts; public WireKind Kind = WireKind.Solid; public double Width;   // model units
        public WireP(List<V3> pts, Lyr layer = Lyr.Cable) { Pts = pts; Layer = layer; }
        public override Mesh ToMesh() => null;
    }

    /// <summary>One placed piece of equipment / cable in the scene.</summary>
    public sealed class SceneItem
    {
        public string ItemId, DefId, Jp, En, Spec;
        public int Qty = 1;
        public bool InBom = true;
        public readonly List<Prim> Prims = new List<Prim>();
        /// <summary>Where the leader arrow points (null = no label).</summary>
        public V3? Anchor;
        /// <summary>Preferred label side in the front view (-1 left, +1 right, 0 auto).</summary>
        public int LabelSide;
        /// <summary>Optional user blocks (base point = Origin) used instead of the generated geometry.</summary>
        public string BlockFront, BlockSide, Block3D;
        public V3? Origin;
        public string Name(bool jp) => jp ? (string.IsNullOrEmpty(Jp) ? En : Jp) : (string.IsNullOrEmpty(En) ? Jp : En);
        public SceneItem Add(Prim p, Lyr layer) { p.Layer = layer; Prims.Add(p); return this; }
        public SceneItem Add(Prim p) { Prims.Add(p); return this; }
    }

    public sealed class Scene
    {
        public readonly List<SceneItem> Items = new List<SceneItem>();
        public double PoleTop, PoleBottom, GroundHalfWidth = 3000;
        public List<string> Warnings = new List<string>();
        /// <summary>Heights (Z) used for the height dimension chain, with a short label.</summary>
        public readonly List<(double z, string jp, string en)> Levels = new List<(double, string, string)>();
        public SceneItem Get(string id) => Items.Find(i => i.ItemId == id);
    }
}
