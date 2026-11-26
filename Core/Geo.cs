using System;
using System.Collections.Generic;

namespace PoleToolApp.Core
{
    /// <summary>3D vector. Model axes: X = right in the front view (direction of the incoming line),
    /// Y = away from the viewer of the front view, Z = up. Origin = pole axis at ground level (GL).</summary>
    public readonly struct V3
    {
        public readonly double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double k) => new V3(a.X * k, a.Y * k, a.Z * k);
        public static V3 operator *(double k, V3 a) => a * k;
        public static V3 operator -(V3 a) => new V3(-a.X, -a.Y, -a.Z);
        public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public V3 Cross(V3 b) => new V3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Len => Math.Sqrt(X * X + Y * Y + Z * Z);
        public V3 Unit { get { var l = Len; return l < 1e-12 ? new V3(0, 0, 1) : this * (1 / l); } }
        public static readonly V3 Zero = new V3(0, 0, 0), UX = new V3(1, 0, 0), UY = new V3(0, 1, 0), UZ = new V3(0, 0, 1);
        public static V3 Lerp(V3 a, V3 b, double t) => a + (b - a) * t;
        public override string ToString() => $"({X:0.#},{Y:0.#},{Z:0.#})";

        /// <summary>Two unit vectors perpendicular to this (non-zero) axis.</summary>
        public void Basis(out V3 u, out V3 v)
        {
            var a = Unit;
            var t = Math.Abs(a.Z) < 0.9 ? UZ : UX;
            u = a.Cross(t).Unit; v = a.Cross(u).Unit;
        }
    }

    public readonly struct P2
    {
        public readonly double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public static P2 operator *(P2 a, double k) => new P2(a.X * k, a.Y * k);
        public double Len => Math.Sqrt(X * X + Y * Y);
        public override string ToString() => $"({X:0.#},{Y:0.#})";
    }

    /// <summary>Orthographic view. Azimuth 0 / elevation 0 = front view (looking along +Y).
    /// Azimuth -90 = right side view (viewer at +X). Elevation 90 = top view.</summary>
    public sealed class View3
    {
        public readonly V3 Right, Up, Toward;   // Toward = from the scene to the viewer
        public readonly double Az, El;
        public View3(double azDeg, double elDeg)
        {
            Az = azDeg; El = elDeg;
            double a = azDeg * Math.PI / 180, e = elDeg * Math.PI / 180;
            var f = new V3(Math.Sin(a) * Math.Cos(e), Math.Cos(a) * Math.Cos(e), -Math.Sin(e));   // forward (into the scene)
            if (Math.Abs(e - Math.PI / 2) < 1e-9) { Right = V3.UX; Up = V3.UY; }
            else { Right = f.Cross(V3.UZ).Unit; Up = Right.Cross(f).Unit; }
            Toward = -f;
        }
        public P2 P(V3 p) => new P2(p.Dot(Right), p.Dot(Up));
        public double Depth(V3 p) => p.Dot(Toward);   // larger = closer to the viewer
        public static readonly View3 Front = new View3(0, 0), Side = new View3(-90, 0), Iso = new View3(-35, 25);
    }

    public static class Hull
    {
        /// <summary>Convex hull (Andrew monotone chain), counter-clockwise.</summary>
        public static List<P2> Of(IEnumerable<P2> pts)
        {
            var p = new List<P2>(pts);
            p.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            if (p.Count < 3) return p;
            var h = new P2[2 * p.Count]; int k = 0;
            for (int i = 0; i < p.Count; i++) { while (k >= 2 && Cr(h[k - 2], h[k - 1], p[i]) <= 1e-9) k--; h[k++] = p[i]; }
            for (int i = p.Count - 2, t = k + 1; i >= 0; i--) { while (k >= t && Cr(h[k - 2], h[k - 1], p[i]) <= 1e-9) k--; h[k++] = p[i]; }
            var r = new List<P2>(k - 1); for (int i = 0; i < k - 1; i++) r.Add(h[i]); return r;
        }
        static double Cr(P2 o, P2 a, P2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        public static double Area(List<P2> poly)
        {
            double s = 0; for (int i = 0; i < poly.Count; i++) { var a = poly[i]; var b = poly[(i + 1) % poly.Count]; s += a.X * b.Y - b.X * a.Y; }
            return Math.Abs(s) / 2;
        }
    }
}
