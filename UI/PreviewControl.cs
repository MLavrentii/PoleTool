using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PoleToolApp.Core;
using Drawing = PoleToolApp.Core.Drawing;

namespace PoleToolApp.UI
{
    /// <summary>Draws a Core drawing with pan / zoom. In orbit mode a left drag changes the view direction.</summary>
    public sealed class PreviewControl : FrameworkElement
    {
        Drawing _d; double _scale = 0.05; Point _origin; bool _needFit = true;
        Point _last; bool _panning, _orbiting;
        public bool OrbitMode;
        public event Action<double, double> Orbit;   // d-azimuth, d-elevation in degrees
        /// <summary>Returns true when the item may be dragged (null = dragging off).</summary>
        public Func<string, bool> CanDrag;
        public event Action<string, P2> DragBegin;
        public event Action<P2, ModifierKeys> DragMove;
        public event Action DragEnd;
        /// <summary>Item drawn highlighted; text shown at the top left while dragging.</summary>
        public string Highlight, Info;
        bool _dragging;
        public bool IsDragging => _dragging;

        public Drawing Drawing { get => _d; set { _d = value; InvalidateVisual(); } }
        public void Fit() { _needFit = true; InvalidateVisual(); }

        public PreviewControl() { ClipToBounds = true; Focusable = true; Cursor = Cursors.Cross; }

        static readonly Dictionary<Lyr, Brush> Col = new Dictionary<Lyr, Brush>
        {
            [Lyr.Pole] = Fz(0x20, 0x20, 0x20), [Lyr.Equip] = Fz(0x10, 0x10, 0x10), [Lyr.Cable] = Fz(0xD0, 0x10, 0x10), [Lyr.Earth] = Fz(0x10, 0x90, 0x10),
            [Lyr.Guy] = Fz(0x50, 0x50, 0x50), [Lyr.Ground] = Fz(0x70, 0x70, 0x70), [Lyr.Text] = Fz(0x10, 0x20, 0xB0), [Lyr.Dim] = Fz(0xA0, 0x10, 0xA0),
            [Lyr.Bom] = Fz(0, 0, 0), [Lyr.Hidden] = Fz(0x90, 0x90, 0x90), [Lyr.Center] = Fz(0x00, 0x99, 0xAA),
        };
        static SolidColorBrush Fz(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
        readonly Dictionary<(Lyr, WireKind), Pen> _pens = new Dictionary<(Lyr, WireKind), Pen>();
        static readonly Pen HiPen = MakeHi();
        static Pen MakeHi() { var p = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)), 2.2); p.Freeze(); return p; }

        /// <summary>AutoCAD colour index -> brush (basic colours; others grey). 7 is drawn black on the white preview.</summary>
        static Brush Aci(int i)
        {
            switch (i)
            {
                case 1: return Brushes.Red; case 2: return Brushes.Goldenrod; case 3: return Brushes.Green; case 4: return Brushes.DarkCyan;
                case 5: return Brushes.Blue; case 6: return Brushes.Magenta; case 7: return Brushes.Black; case 8: return Brushes.Gray;
                case 9: return Brushes.Silver; case 30: return Brushes.DarkOrange; default: return Brushes.DimGray;
            }
        }
        readonly Dictionary<(int, WireKind), Pen> _aciPens = new Dictionary<(int, WireKind), Pen>();
        Pen PenFor(DLine l)
        {
            if (l.Item != null && l.Item == Highlight) return HiPen;
            if (l.Color <= 0) return PenFor(l.Layer, l.Kind);
            if (_aciPens.TryGetValue((l.Color, l.Kind), out var p)) return p;
            var b = PenFor(l.Layer, l.Kind);
            p = new Pen(Aci(l.Color), b.Thickness) { DashStyle = b.DashStyle }; p.Freeze();
            _aciPens[(l.Color, l.Kind)] = p; return p;
        }

        Pen PenFor(Lyr l, WireKind k)
        {
            if (_pens.TryGetValue((l, k), out var p)) return p;
            p = new Pen(Col.TryGetValue(l, out var b) ? b : Brushes.Black, l == Lyr.Pole || l == Lyr.Cable ? 1.1 : 0.8);
            if (k == WireKind.Hidden) p.DashStyle = new DashStyle(new double[] { 5, 3 }, 0);
            else if (k == WireKind.Center) p.DashStyle = new DashStyle(new double[] { 12, 3, 2, 3 }, 0);
            else if (k == WireKind.Phantom) p.DashStyle = new DashStyle(new double[] { 12, 3, 2, 3, 2, 3 }, 0);
            p.Freeze(); _pens[(l, k)] = p; return p;
        }

        Point S(P2 p) => new Point(_origin.X + p.X * _scale, _origin.Y - p.Y * _scale);
        P2 M(Point s) => new P2((s.X - _origin.X) / _scale, (_origin.Y - s.Y) / _scale);

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_d == null || ActualWidth < 10 || ActualHeight < 10) return;
            if (_needFit)
            {
                _d.Bounds(out var x0, out var y0, out var x1, out var y1);
                double w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
                _scale = Math.Min((ActualWidth - 30) / w, (ActualHeight - 30) / h);
                _origin = new Point(ActualWidth / 2 - (x0 + x1) / 2 * _scale, ActualHeight / 2 + (y0 + y1) / 2 * _scale);
                _needFit = false;
            }
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var face = new Typeface(new FontFamily("Meiryo UI, Yu Gothic UI, MS Gothic, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            foreach (var e in _d.E)
            {
                switch (e)
                {
                    case DMask m:
                        dc.DrawGeometry(Brushes.White, null, Poly(m.Pts, true)); break;
                    case DLine l:
                        {
                            var pen = PenFor(l);
                            if (l.Width > 0) { pen = new Pen(pen.Brush, Math.Max(1, l.Width * _scale)); }
                            dc.DrawGeometry(null, pen, Poly(l.Pts, l.Closed)); break;
                        }
                    case DText t:
                        {
                            double fs = t.H * _scale * 1.05; if (fs < 1.2) break;
                            var ft = new FormattedText(t.S ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, fs, Col[t.Layer], dpi);
                            var p = S(t.Pos);
                            double x = t.Align == TAlign.Left ? p.X : t.Align == TAlign.Center ? p.X - ft.Width / 2 : p.X - ft.Width;
                            dc.DrawText(ft, new Point(x, p.Y - ft.Baseline));
                            break;
                        }
                    case DCircle c:
                        dc.DrawEllipse(c.Filled ? Col[c.Layer] : null, PenFor(c.Layer, WireKind.Solid), S(c.C), c.R * _scale, c.R * _scale); break;
                    case DDim dd: DrawDim(dc, dd, face, dpi); break;
                    case DTable tb: DrawTable(dc, tb, face, dpi); break;
                    case DLeader ld:
                        {
                            var pen = ld.Item != null && ld.Item == Highlight ? HiPen : PenFor(Lyr.Text, WireKind.Solid);
                            var a = S(ld.Anchor); var l = S(ld.Landing); var en = S(ld.End);
                            dc.DrawLine(pen, a, l); dc.DrawLine(pen, l, en);
                            double r = Math.Max(1.5, ld.H * 0.18 * _scale); dc.DrawEllipse(Col[Lyr.Text], null, a, r, r);
                            double fs = ld.H * _scale * 1.05; if (fs < 1.2) break;
                            var ft = new FormattedText(ld.S ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, fs, Col[Lyr.Text], dpi);
                            double x = ld.Side > 0 ? l.X + ld.H * 0.3 * _scale : l.X - ld.H * 0.3 * _scale - ft.Width;
                            dc.DrawText(ft, new Point(x, l.Y - ld.H * 0.3 * _scale - ft.Baseline));
                            break;
                        }
                    case DInsert bi:
                        {
                            var p = S(bi.Pos); var pen = PenFor(Lyr.Equip, WireKind.Solid);
                            dc.DrawLine(pen, new Point(p.X - 6, p.Y), new Point(p.X + 6, p.Y)); dc.DrawLine(pen, new Point(p.X, p.Y - 6), new Point(p.X, p.Y + 6));
                            break;
                        }
                }
            }
            DrawInfo(dc);
        }

        void DrawInfo(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(Info)) return;
            var ft = new FormattedText(Info, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Meiryo UI, Segoe UI"), 12, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xF4, 0xD6)), new Pen(Brushes.DarkOrange, 1), new Rect(6, 6, ft.Width + 12, ft.Height + 8));
            dc.DrawText(ft, new Point(12, 10));
        }

        /// <summary>Item under the cursor: topmost wipeout containing the point, or a line within 5 px.</summary>
        public string HitItem(Point s)
        {
            if (_d == null) return null;
            var m = M(s); double tol = 5 / _scale;
            for (int i = _d.E.Count - 1; i >= 0; i--)
            {
                var e = _d.E[i];
                if (e is DMask mk) { if (Inside(mk.Pts, m)) return mk.Item; }
                else if (e is DLine l && l.Item != null)
                    for (int k = 0; k + 1 < l.Pts.Count; k++) if (SegDist(l.Pts[k], l.Pts[k + 1], m) < tol) return l.Item;
            }
            return null;
        }

        static bool Inside(List<P2> poly, P2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
            return c;
        }

        static double SegDist(P2 a, P2 b, P2 p)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y; return Math.Sqrt(x * x + y * y);
        }

        StreamGeometry Poly(List<P2> pts, bool closed)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(S(pts[0]), closed, closed);
                for (int i = 1; i < pts.Count; i++) c.LineTo(S(pts[i]), true, false);
            }
            g.Freeze(); return g;
        }

        void DrawDim(DrawingContext dc, DDim d, Typeface face, double dpi)
        {
            var pen = PenFor(Lyr.Dim, WireKind.Solid);
            if (!d.Vertical) return;
            var a = S(new P2(d.At, d.A.Y)); var b = S(new P2(d.At, d.B.Y));
            dc.DrawLine(pen, a, b);
            dc.DrawLine(pen, a, S(d.A)); dc.DrawLine(pen, b, S(d.B));
            foreach (var p in new[] { a, b }) dc.DrawLine(pen, new Point(p.X - 3, p.Y + 3), new Point(p.X + 3, p.Y - 3));
            double fs = Math.Max(7, 120 * _scale * 1.5); if (fs > 14) fs = 14;
            var ft = new FormattedText(d.Override ?? Math.Abs(d.A.Y - d.B.Y).ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, face, fs, Col[Lyr.Dim], dpi);
            if (Math.Abs(a.Y - b.Y) < ft.Width + 4) return;
            dc.PushTransform(new RotateTransform(-90, a.X - 2, (a.Y + b.Y) / 2));
            dc.DrawText(ft, new Point(a.X - 2 - ft.Width / 2, (a.Y + b.Y) / 2 - ft.Height));
            dc.Pop();
        }

        void DrawTable(DrawingContext dc, DTable t, Typeface face, double dpi)
        {
            var pen = PenFor(Lyr.Bom, WireKind.Solid);
            double w = 0; foreach (var c in t.ColW) w += c;
            int n = t.Rows.Count;
            for (int i = 0; i <= n; i++) dc.DrawLine(pen, S(new P2(t.TopLeft.X, t.TopLeft.Y - i * t.RowH)), S(new P2(t.TopLeft.X + w, t.TopLeft.Y - i * t.RowH)));
            double x = t.TopLeft.X;
            for (int c = 0; c <= t.ColW.Length; c++)
            {
                dc.DrawLine(pen, S(new P2(x, t.TopLeft.Y)), S(new P2(x, t.TopLeft.Y - n * t.RowH)));
                if (c < t.ColW.Length) x += t.ColW[c];
            }
            double fs = t.TextH * _scale * 1.0; if (fs < 1.2) return;
            var title = new FormattedText(t.Title ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, fs * 1.2, Brushes.Black, dpi);
            var tp = S(new P2(t.TopLeft.X, t.TopLeft.Y + t.RowH * 0.3)); dc.DrawText(title, new Point(tp.X, tp.Y - title.Baseline));
            for (int r = 0; r < n; r++)
            {
                x = t.TopLeft.X;
                for (int c = 0; c < t.ColW.Length; c++)
                {
                    var ft = new FormattedText(t.Rows[r][c] ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, fs, Brushes.Black, dpi)
                    { MaxTextWidth = Math.Max(1, (t.ColW[c] - t.TextH * 0.6) * _scale), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                    var p = S(new P2(x + t.TextH * 0.4, t.TopLeft.Y - (r + 0.72) * t.RowH));
                    dc.DrawText(ft, new Point(p.X, p.Y - ft.Baseline));
                    x += t.ColW[c];
                }
            }
        }

        // ------------------------------------------------------------------ mouse

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var p = e.GetPosition(this); var m = M(p);
            _scale *= e.Delta > 0 ? 1.2 : 1 / 1.2;
            _origin = new Point(p.X - m.X * _scale, p.Y + m.Y * _scale);
            InvalidateVisual(); e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            if (e.ClickCount == 2) { Fit(); return; }
            _last = e.GetPosition(this);
            if (e.ChangedButton == MouseButton.Left && CanDrag != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {   // Ctrl + drag on an element = move it (also in the 3D and sheet views)
                var id = HitItem(_last);
                if (id != null && CanDrag(id)) { _orbiting = false; _dragging = true; Highlight = id; DragBegin?.Invoke(id, M(_last)); }
                else if (!_orbiting) _panning = true;
            }
            else if (e.ChangedButton == MouseButton.Left && (OrbitMode || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) _orbiting = true;
            else _panning = true;
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_dragging) { DragMove?.Invoke(M(p), Keyboard.Modifiers); return; }
            if (!_panning && !_orbiting)
            {
                string id = CanDrag == null ? null : HitItem(p);
                if (id != null && !CanDrag(id)) id = null;
                Cursor = id != null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? Cursors.SizeAll : id != null ? Cursors.Hand : Cursors.Cross;
                if (id != Highlight) { Highlight = id; InvalidateVisual(); }
                return;
            }
            var d = p - _last; _last = p;
            // Shift: turn only around the vertical pole axis (elevation kept)
            if (_orbiting) Orbit?.Invoke(-d.X * 0.5, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0 : d.Y * 0.5);
            else { _origin += d; InvalidateVisual(); }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            bool was = _dragging, orb = _orbiting;
            _panning = _orbiting = _dragging = false; ReleaseMouseCapture();
            if (orb) { Info = null; InvalidateVisual(); }
            if (was) { Info = null; DragEnd?.Invoke(); InvalidateVisual(); }
        }

        /// <summary>Stops a drag without the DragEnd event (used by Esc).</summary>
        public void AbortDrag() { _dragging = false; Info = null; ReleaseMouseCapture(); InvalidateVisual(); }

        protected override void OnMouseLeave(MouseEventArgs e) { if (!_dragging && Highlight != null) { Highlight = null; InvalidateVisual(); } }
    }
}
