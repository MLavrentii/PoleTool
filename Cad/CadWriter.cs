using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PoleToolApp.Core;

namespace PoleToolApp.Cad
{
    /// <summary>Writes a Core drawing / scene into the AutoCAD database.</summary>
    public static class CadWriter
    {
        public const string App = "PT_POLETOOL";
        const string TextStyle = "PT_TEXT";

        static readonly Dictionary<Lyr, (string name, short color, string ltype)> LayerDefs = new()
        {
            [Lyr.Pole] = ("POLE", 7, null), [Lyr.Equip] = ("EQUIP", 7, null), [Lyr.Cable] = ("CABLE", 1, null),
            [Lyr.Earth] = ("EARTH", 3, null), [Lyr.Guy] = ("GUY", 8, null), [Lyr.Ground] = ("GROUND", 8, null),
            [Lyr.Text] = ("TEXT", 5, null), [Lyr.Dim] = ("DIM", 6, null), [Lyr.Bom] = ("BOM", 7, null),
            [Lyr.Hidden] = ("HIDDEN", 8, "HIDDEN"), [Lyr.Center] = ("CENTER", 4, "CENTER"),
        };

        // ------------------------------------------------------------------ public entry points

        /// <summary>Asks for a point and inserts the 2D sheet (one block or loose entities).</summary>
        public static void Insert2D(Document doc, PoleJob job) =>
            InsertDrawing(doc, job, (scene, exists) => Sheet.Compose(job, scene, out _, exists), "");

        /// <summary>Inserts the current 3D preview direction as a clean 2D line drawing (hidden lines cut, duplicates merged).</summary>
        public static void InsertView(Document doc, PoleJob job) =>
            InsertDrawing(doc, job, (scene, exists) => Sheet.ViewOnly(job, scene, job.ViewAz, job.ViewEl, exists), "_3DVIEW");

        static void InsertDrawing(Document doc, PoleJob job, Func<Scene, Func<string, bool>, Drawing> make, string suffix)
        {
            var ed = doc.Editor; var db = doc.Database;
            var pr = ed.GetPoint(new PromptPointOptions(job.LabelsJp ? "\n挿入点（GL・柱中心）: " : "\nInsertion point (pole axis at GL): "));
            if (pr.Status != PromptStatus.OK) return;
            var ucs = ed.CurrentUserCoordinateSystem; var pt = pr.Value.TransformBy(ucs);
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var scene = SceneBuilder.Build(job);
                var drawing = make(scene, name => BlockExists(db, tr, name));
                Prepare(db, tr, job);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                if (job.AsBlock)
                {
                    var btab = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    string name = UniqueName(btab, "PT_" + Safe(string.IsNullOrWhiteSpace(job.Title) ? job.Pole.Designation(false) : job.Title) + suffix);
                    var bt = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
                    btab.Add(bt); tr.AddNewlyCreatedDBObject(bt, true);
                    Write(db, tr, bt, drawing, job, Matrix3d.Identity);
                    var br = new BlockReference(pt, bt.ObjectId) { Layer = "0" };
                    ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                    WriteXData(db, tr, br, job);
                }
                else
                {
                    var ids = Write(db, tr, ms, drawing, job, Matrix3d.Displacement(pt - Point3d.Origin));
                    var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
                    var g = new Group("Pole Tool", true);
                    gd.SetAt("*", g); tr.AddNewlyCreatedDBObject(g, true);
                    g.Append(new ObjectIdCollection(ids.ToArray()));
                    if (ids.Count > 0) WriteXData(db, tr, tr.GetObject(ids[0], OpenMode.ForWrite), job);
                }
                tr.Commit();
                foreach (var w in scene.Warnings) ed.WriteMessage("\nPoleTool: " + w);
            }
        }

        /// <summary>Asks for a point and inserts the pole as 3D solids (one block).</summary>
        public static void Insert3D(Document doc, PoleJob job)
        {
            var ed = doc.Editor; var db = doc.Database;
            var pr = ed.GetPoint(new PromptPointOptions(job.LabelsJp ? "\n3Dモデルの挿入点（GL・柱中心）: " : "\n3D model insertion point (pole axis at GL): "));
            if (pr.Status != PromptStatus.OK) return;
            var pt = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Prepare(db, tr, job);
                var scene = SceneBuilder.Build(job);
                var btab = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var bt = new BlockTableRecord { Name = UniqueName(btab, "PT3D_" + Safe(job.Pole.Designation(false))) };
                btab.Add(bt); tr.AddNewlyCreatedDBObject(bt, true);
                int failed = 0;
                foreach (var item in scene.Items)
                {
                    if (item.Block3D != null && item.Origin.HasValue && BlockExists(db, tr, item.Block3D))
                    {
                        var o = item.Origin.Value;
                        var ib = new BlockReference(new Point3d(o.X, o.Y, o.Z), btab[item.Block3D]) { Layer = LayerName(job, Lyr.Equip) };
                        bt.AppendEntity(ib); tr.AddNewlyCreatedDBObject(ib, true); continue;
                    }
                    foreach (var p in item.Prims)
                    {
                        Entity e = null;
                        try { e = Solid(p); } catch { failed++; }
                        if (e == null) continue;
                        e.Layer = LayerName(job, p.Layer);
                        if (p.Color > 0) e.ColorIndex = p.Color;
                        bt.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
                    }
                }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(pt, bt.ObjectId) { Layer = "0" };
                ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                WriteXData(db, tr, br, job);
                tr.Commit();
                if (failed > 0) ed.WriteMessage($"\nPoleTool: {failed} solid(s) could not be created.");
                // look at the new model from the same direction as the preview
                try
                {
                    var t = new View3(job.ViewAz, job.ViewEl).Toward;
                    using (var vtr = ed.GetCurrentView())
                    {
                        vtr.ViewDirection = new Vector3d(t.X, t.Y, t.Z);
                        vtr.Target = pt + new Vector3d(0, 0, job.Pole.Top / 2);
                        ed.SetCurrentView(vtr);
                    }
                    doc.SendStringToExecute("_.ZOOM _E ", true, false, false);
                }
                catch { }
                ed.WriteMessage(job.LabelsJp ? "\nPoleTool: 3Dモデルを作成しました（表示スタイルを「シェード」にすると確認しやすくなります）。" : "\nPoleTool: 3D model created (use a shaded visual style to view it).");
            }
        }

        // ------------------------------------------------------------------ 2D entities

        static List<ObjectId> Write(Database db, Transaction tr, BlockTableRecord owner, Drawing d, PoleJob job, Matrix3d xf)
        {
            var ids = new List<ObjectId>();
            string wipeLayer = string.IsNullOrWhiteSpace(job.WipeoutLayer) ? "01_WIPEOUT" : job.WipeoutLayer;
            var styleId = ((TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead))[TextStyle];
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            void Add(Entity e, Lyr l, int color)
            {
                if (!(e is Wipeout)) e.Layer = LayerName(job, l);
                if (color > 0) e.ColorIndex = color;
                e.TransformBy(xf);
                ids.Add(owner.AppendEntity(e)); tr.AddNewlyCreatedDBObject(e, true);
                if (e is DBText dt && dt.HorizontalMode != TextHorizontalMode.TextLeft) dt.AdjustAlignment(db);
            }
            foreach (var en in d.E)
            {
                switch (en)
                {
                    case DMask m when job.Masks:
                        {
                            var pc = new Point2dCollection(); foreach (var p in m.Pts) pc.Add(new Point2d(p.X, p.Y)); pc.Add(new Point2d(m.Pts[0].X, m.Pts[0].Y));
                            try
                            {
                                var w = new Wipeout(); w.SetDatabaseDefaults(db); w.SetFrom(pc, Vector3d.ZAxis); w.Layer = wipeLayer;
                                Add(w, Lyr.Equip, 0);
                            }
                            catch { }
                            break;
                        }
                    case DLine l:
                        {
                            var pl = new Polyline(l.Pts.Count);
                            for (int i = 0; i < l.Pts.Count; i++) pl.AddVertexAt(i, new Point2d(l.Pts[i].X, l.Pts[i].Y), 0, 0, 0);
                            pl.Closed = l.Closed; if (l.Width > 0) pl.ConstantWidth = l.Width;
                            var lyr = l.Layer;
                            if (l.Kind == WireKind.Hidden || l.Kind == WireKind.Center || l.Kind == WireKind.Phantom)
                            {
                                string lt = l.Kind == WireKind.Hidden ? "HIDDEN" : l.Kind == WireKind.Center ? "CENTER" : "PHANTOM";
                                if (EnsureLinetype(db, tr, lt)) { pl.Linetype = lt; pl.LinetypeScale = job.Scale * 0.5; pl.Plinegen = true; }
                            }
                            Add(pl, lyr, l.Color);
                            break;
                        }
                    case DText t:
                        {
                            var tx = new DBText { TextString = t.S ?? "", Height = t.H, Position = new Point3d(t.Pos.X, t.Pos.Y, 0), Rotation = t.Rot, TextStyleId = styleId };
                            if (t.Align != TAlign.Left)
                            {
                                tx.HorizontalMode = t.Align == TAlign.Center ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight;
                                tx.AlignmentPoint = tx.Position;
                            }
                            Add(tx, t.Layer, t.Color);
                            break;
                        }
                    case DCircle c:
                        if (c.Filled)
                        {
                            var dot = new Polyline(2);
                            dot.AddVertexAt(0, new Point2d(c.C.X - c.R / 2, c.C.Y), 1, c.R, c.R);
                            dot.AddVertexAt(1, new Point2d(c.C.X + c.R / 2, c.C.Y), 1, c.R, c.R);
                            dot.Closed = true; Add(dot, c.Layer, c.Color);
                        }
                        else Add(new Circle(new Point3d(c.C.X, c.C.Y, 0), Vector3d.ZAxis, c.R), c.Layer, c.Color);
                        break;
                    case DDim dd:
                        {
                            var p1 = new Point3d(dd.A.X, dd.A.Y, 0); var p2 = new Point3d(dd.B.X, dd.B.Y, 0);
                            var dl = dd.Vertical ? new Point3d(dd.At, (dd.A.Y + dd.B.Y) / 2, 0) : new Point3d((dd.A.X + dd.B.X) / 2, dd.At, 0);
                            var dim = new RotatedDimension(dd.Vertical ? Math.PI / 2 : 0, p1, p2, dl, dd.Override ?? "", db.Dimstyle);
                            dim.Dimscale = job.Scale;
                            Add(dim, dd.Layer, dd.Color);
                            break;
                        }
                    case DLeader ld:
                        {
                            bool done = false;
                            if (job.MLeaders)
                                try { Add(MakeLeader(db, ld, styleId), ld.Layer, ld.Color); done = true; }
                                catch { done = false; }
                            if (!done)
                            {   // fallback: dot + polyline + text
                                var pl = new Polyline(3);
                                pl.AddVertexAt(0, new Point2d(ld.Anchor.X, ld.Anchor.Y), 0, 0, 0);
                                pl.AddVertexAt(1, new Point2d(ld.Landing.X, ld.Landing.Y), 0, 0, 0);
                                pl.AddVertexAt(2, new Point2d(ld.End.X, ld.End.Y), 0, 0, 0);
                                Add(pl, ld.Layer, ld.Color);
                                double r = ld.H * 0.18;
                                var dot = new Polyline(2);
                                dot.AddVertexAt(0, new Point2d(ld.Anchor.X - r / 2, ld.Anchor.Y), 1, r, r);
                                dot.AddVertexAt(1, new Point2d(ld.Anchor.X + r / 2, ld.Anchor.Y), 1, r, r);
                                dot.Closed = true; Add(dot, ld.Layer, ld.Color);
                                var tx = new DBText { TextString = ld.S ?? "", Height = ld.H, TextStyleId = styleId,
                                    Position = new Point3d(ld.Landing.X + ld.Side * ld.H * 0.3, ld.Landing.Y + ld.H * 0.3, 0) };
                                if (ld.Side < 0) { tx.HorizontalMode = TextHorizontalMode.TextRight; tx.AlignmentPoint = tx.Position; }
                                Add(tx, ld.Layer, ld.Color);
                            }
                            break;
                        }
                    case DTable tb:
                        Add(Table(db, tr, tb, styleId), tb.Layer, tb.Color);
                        break;
                    case DInsert bi:
                        if (bt.Has(bi.Name)) Add(new BlockReference(new Point3d(bi.Pos.X, bi.Pos.Y, 0), bt[bi.Name]) { Rotation = bi.Rot }, bi.Layer, 0);
                        break;
                }
            }
            return ids;
        }

        /// <summary>AutoCAD multileader: small dot at the equipment, straight leader, text sitting on an underline.</summary>
        static MLeader MakeLeader(Database db, DLeader ld, ObjectId styleId)
        {
            var ml = new MLeader();
            ml.SetDatabaseDefaults(db);
            ml.ContentType = ContentType.MTextContent;
            var mt = new MText();
            mt.SetDatabaseDefaults(db);
            mt.TextStyleId = styleId;
            mt.TextHeight = ld.H;
            mt.Contents = (ld.S ?? "").Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
            mt.Location = new Point3d(ld.Landing.X + ld.Side * ld.H * 0.3, ld.Landing.Y, 0);
            ml.MText = mt;
            ml.TextHeight = ld.H;
            int li = ml.AddLeader();
            int ln = ml.AddLeaderLine(li);
            ml.AddFirstVertex(ln, new Point3d(ld.Anchor.X, ld.Anchor.Y, 0));
            ml.AddLastVertex(ln, new Point3d(ld.Landing.X, ld.Landing.Y, 0));
            ml.LeaderLineType = LeaderType.StraightLeader;
            ml.ArrowSize = ld.H * 0.5;
            try
            {
                var bt = (BlockTable)db.BlockTableId.GetObject(OpenMode.ForRead);
                if (bt.Has("_DotSmall")) ml.ArrowSymbolId = bt["_DotSmall"];
            }
            catch { }
            ml.EnableLanding = true;
            ml.EnableDogleg = true;
            ml.DoglegLength = Math.Max(ld.H * 0.5, Math.Abs(ld.End.X - ld.Landing.X) - ld.H * 0.3 - ml.MText.ActualWidth);
            ml.LandingGap = ld.H * 0.3;
            ml.SetDogleg(li, new Vector3d(ld.Side >= 0 ? 1 : -1, 0, 0));
            // Japanese style: text on top of an underline that runs the full text length
            ml.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
            ml.SetTextAttachmentType(TextAttachmentType.AttachmentBottomLine, LeaderDirectionType.LeftLeader);
            ml.SetTextAttachmentType(TextAttachmentType.AttachmentBottomLine, LeaderDirectionType.RightLeader);
            try { ml.ExtendLeaderToText = true; } catch { }
            try { ml.TextAlignmentType = ld.Side >= 0 ? TextAlignmentType.LeftAlignment : TextAlignmentType.RightAlignment; } catch { }
            return ml;
        }

        static Table Table(Database db, Transaction tr, DTable tb, ObjectId styleId)
        {
            int cols = tb.ColW.Length, rows = tb.Rows.Count + 1;
            var t = new Table();
            t.SetDatabaseDefaults(db);
            t.TableStyle = db.Tablestyle;
            t.SetSize(rows, cols);
            t.Position = new Point3d(tb.TopLeft.X, tb.TopLeft.Y, 0);
            for (int c = 0; c < cols; c++) t.Columns[c].Width = tb.ColW[c];
            for (int r = 0; r < rows; r++) t.Rows[r].Height = tb.RowH;
            try { t.MergeCells(CellRange.Create(t, 0, 0, 0, cols - 1)); } catch { }
            Cell(t, 0, 0, tb.Title, tb.TextH * 1.2, styleId, CellAlignment.MiddleCenter);
            for (int r = 0; r < tb.Rows.Count; r++)
                for (int c = 0; c < cols; c++)
                    Cell(t, r + 1, c, tb.Rows[r][c], tb.TextH, styleId, c == 0 || c == 3 ? CellAlignment.MiddleCenter : CellAlignment.MiddleLeft);
            t.GenerateLayout();
            return t;
        }

        static void Cell(Table t, int r, int c, string s, double h, ObjectId style, CellAlignment a)
        {
            var cell = t.Cells[r, c];
            cell.TextStyleId = style; cell.TextHeight = h; cell.Alignment = a; cell.TextString = s ?? "";
        }

        // ------------------------------------------------------------------ 3D solids

        static Entity Solid(Prim p)
        {
            switch (p)
            {
                case BoxP b:
                    {
                        var s = new Solid3d(); s.SetDatabaseDefaults();
                        s.CreateBox(Math.Max(b.Sx, 0.1), Math.Max(b.Sy, 0.1), Math.Max(b.Sz, 0.1));
                        s.TransformBy(Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                            P(b.C), V(b.Ax), V(b.Ay), V(b.Az)));
                        return s;
                    }
                case FrustumP f:
                    {
                        var ax = f.B - f.A; double h = ax.Len; if (h < 0.1) return null;
                        var s = new Solid3d(); s.SetDatabaseDefaults();
                        s.CreateFrustum(h, f.R1, f.R1, f.R2);
                        ax.Basis(out var u, out var v);
                        var z = ax.Unit; var x = u; var y = z.Cross(x);
                        s.TransformBy(Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                            P((f.A + f.B) * 0.5), V(x), V(y), V(z)));
                        return s;
                    }
                case PrismP pr:
                    {
                        var n = pr.U.Cross(pr.V); double h = pr.E.Dot(n.Unit);
                        if (Math.Abs(h) < 0.1) return null;
                        var pl = new Polyline(pr.Profile.Count);
                        for (int i = 0; i < pr.Profile.Count; i++) pl.AddVertexAt(i, new Point2d(pr.Profile[i].X, pr.Profile[i].Y), 0, 0, 0);
                        pl.Closed = true;
                        var curves = new DBObjectCollection { pl };
                        var regs = Region.CreateFromCurves(curves);
                        if (regs.Count == 0) return null;
                        var reg = (Region)regs[0];
                        var s = new Solid3d(); s.SetDatabaseDefaults();
                        s.Extrude(reg, h, 0);
                        reg.Dispose(); pl.Dispose();
                        var w = n.Unit; var vv = w.Cross(pr.U.Unit);
                        s.TransformBy(Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                            P(pr.O), V(pr.U.Unit), V(vv), V(w)));
                        return s;
                    }
                case WireP wp:
                    {
                        var pts = new Point3dCollection(); foreach (var q in wp.Pts) pts.Add(P(q));
                        return new Polyline3d(Poly3dType.SimplePoly, pts, false);
                    }
            }
            return null;
        }

        static Point3d P(V3 v) => new Point3d(v.X, v.Y, v.Z);
        static Vector3d V(V3 v) => new Vector3d(v.X, v.Y, v.Z);

        // ------------------------------------------------------------------ tables, layers, xdata

        static void Prepare(Database db, Transaction tr, PoleJob job)
        {
            foreach (var kv in LayerDefs)
            {
                string lt = kv.Value.ltype; if (lt != null && !EnsureLinetype(db, tr, lt)) lt = null;
                EnsureLayer(db, tr, LayerName(job, kv.Key), kv.Value.color, lt);
            }
            if (job.Masks) { EnsureLayer(db, tr, string.IsNullOrWhiteSpace(job.WipeoutLayer) ? "01_WIPEOUT" : job.WipeoutLayer, 7, null); EnsureWipeoutSupport(); }
            var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (!ts.Has(TextStyle))
            {
                ts.UpgradeOpen();
                var r = new TextStyleTableRecord { Name = TextStyle, FileName = "msgothic.ttc", XScale = 0.85 };
                ts.Add(r); tr.AddNewlyCreatedDBObject(r, true);
            }
        }

        public static string LayerName(PoleJob job, Lyr l) => (job.LayerPrefix ?? "") + LayerDefs[l].name;

        static void EnsureLayer(Database db, Transaction tr, string name, short color, string ltype)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            try { SymbolUtilityServices.ValidateSymbolName(name, false); } catch { return; }
            var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
            if (ltype != null) rec.LinetypeObjectId = ((LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead))[ltype];
            lt.UpgradeOpen(); lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
        }

        static bool EnsureLinetype(Database db, Transaction tr, string name)
        {
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (lt.Has(name)) return true;
            foreach (var f in new[] { "acadiso.lin", "acad.lin" }) { try { db.LoadLineTypeFile(name, f); return true; } catch { } }
            return false;
        }

        static bool _wipeoutLoaded;
        static void EnsureWipeoutSupport()
        {
            if (_wipeoutLoaded) return; _wipeoutLoaded = true;
            try { SystemObjects.DynamicLinker.LoadModule("AcWipeoutObj.dbx", false, false); } catch { }
        }

        static bool BlockExists(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead)).Has(name);
        }

        static string Safe(string s)
        {
            var sb = new StringBuilder(); foreach (var c in s ?? "") sb.Append("<>/\\\":;?*|,=`".IndexOf(c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "POLE" : sb.ToString();
        }

        static string UniqueName(BlockTable bt, string baseName)
        {
            string n = baseName; int i = 1;
            while (bt.Has(n)) n = baseName + "_" + (++i);
            return n;
        }

        /// <summary>Stores the job JSON in an Xrecord of the object's extension dictionary (no 16 KB XData limit).</summary>
        public static void WriteXData(Database db, Transaction tr, DBObject obj, PoleJob job)
        {
            string json = job.ToJson();
            if (!obj.IsWriteEnabled) obj.UpgradeOpen();
            if (obj.ExtensionDictionary.IsNull) obj.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForWrite);
            var rb = new ResultBuffer();
            for (int i = 0; i < json.Length; i += 250)
                rb.Add(new TypedValue((int)DxfCode.Text, json.Substring(i, Math.Min(250, json.Length - i))));
            if (dict.Contains(App))
            {
                var old = (Xrecord)tr.GetObject(dict.GetAt(App), OpenMode.ForWrite); old.Data = rb;
            }
            else
            {
                var xr = new Xrecord { Data = rb };
                dict.SetAt(App, xr); tr.AddNewlyCreatedDBObject(xr, true);
            }
        }

        public static PoleJob ReadXData(Transaction tr, DBObject obj)
        {
            if (obj.ExtensionDictionary.IsNull) return null;
            var dict = (DBDictionary)tr.GetObject(obj.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(App)) return null;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(App), OpenMode.ForRead);
            if (xr.Data == null) return null;
            var sb = new StringBuilder();
            foreach (TypedValue tv in xr.Data) if (tv.TypeCode == (int)DxfCode.Text) sb.Append((string)tv.Value);
            return PoleJob.FromJson(sb.ToString());
        }
    }
}
