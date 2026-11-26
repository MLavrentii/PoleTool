using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PoleToolApp.Cad;
using PoleToolApp.Core;
using PoleToolApp.UI;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(PoleToolApp.Plugin))]
[assembly: CommandClass(typeof(PoleToolApp.Commands))]

namespace PoleToolApp
{
    public class Plugin : IExtensionApplication
    {
        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PoleTool");

        public void Initialize()
        {
            try
            {
                Catalog.UserFile = Path.Combine(DataDir, "library.json");
                Configs.UserDir = Path.Combine(DataDir, "configs");
                RibbonUi.Create();
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    "\nPole Tool loaded. Commands: POLETOOL (window), POLEEDIT (edit a placed pole).");
            }
            catch (System.Exception ex)
            {
                AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nPole Tool init error: " + ex.Message);
            }
        }

        public void Terminate() { }
    }

    public class Commands
    {
        static PoleToolWindow _window;
        internal static PoleJob PendingJob;

        internal static PoleToolWindow ShowWindow()
        {
            if (_window == null || !_window.IsLoaded)
            {
                _window = new PoleToolWindow();
                _window.Closed += (s, e) => _window = null;
                AcApp.ShowModelessWindow(_window);
            }
            else _window.Activate();
            return _window;
        }

        [CommandMethod("PTOOL", "POLETOOL", CommandFlags.Modal)]
        public void PoleTool() => ShowWindow();

        [CommandMethod("PTOOL", "POLEEDIT", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void PoleEdit()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor;
            ObjectId id = ObjectId.Null;
            var sel = ed.SelectImplied();
            if (sel.Status == PromptStatus.OK && sel.Value.Count == 1) id = sel.Value[0].ObjectId;
            else
            {
                var r = ed.GetEntity("\nSelect a Pole Tool block / 装柱図ブロックを選択: ");
                if (r.Status != PromptStatus.OK) return;
                id = r.ObjectId;
            }
            PoleJob job;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                job = CadWriter.ReadXData(tr, tr.GetObject(id, OpenMode.ForRead));
                tr.Commit();
            }
            if (job == null) { ed.WriteMessage("\nThis object was not created by the Pole Tool."); return; }
            ShowWindow().LoadJob(job);
        }

        [CommandMethod("PTOOL", "PT_INSERT2D", CommandFlags.Modal | CommandFlags.NoHistory)]
        public void Insert2D() => RunPending(CadWriter.Insert2D);

        [CommandMethod("PTOOL", "PT_INSERTVIEW", CommandFlags.Modal | CommandFlags.NoHistory)]
        public void InsertView() => RunPending(CadWriter.InsertView);

        [CommandMethod("PTOOL", "PT_INSERT3D", CommandFlags.Modal | CommandFlags.NoHistory)]
        public void Insert3D() => RunPending(CadWriter.Insert3D);

        static void RunPending(Action<Document, PoleJob> action)
        {
            var job = PendingJob; PendingJob = null;
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (job == null || doc == null) return;
            try { action(doc, job); }
            catch (System.Exception ex) { doc.Editor.WriteMessage($"\nPoleTool error: {ex.Message}"); }
        }

        /// <summary>Hands the job from the modeless window to a command (safe document access).</summary>
        internal static void Run(string command, PoleJob job)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            PendingJob = job.Clone();
            doc.SendStringToExecute($"_{command} ", true, false, false);
        }

        /// <summary>Block names of the active drawing (for the library editor).</summary>
        internal static System.Collections.Generic.List<string> BlockNames()
        {
            var list = new System.Collections.Generic.List<string>();
            var doc = AcApp.DocumentManager.MdiActiveDocument; if (doc == null) return list;
            using var tr = doc.Database.TransactionManager.StartOpenCloseTransaction();
            var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in bt)
            {
                var r = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (!r.IsLayout && !r.IsAnonymous && !r.IsFromExternalReference) list.Add(r.Name);
            }
            tr.Commit();
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }
}
