using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PoleToolApp.Core;
using Drawing = PoleToolApp.Core.Drawing;

namespace PoleToolApp.UI
{
    /// <summary>Main window, built in code (no XAML). Rebuilt completely when the UI language changes.</summary>
    public sealed class PoleToolWindow : Window
    {
        PoleJob _job;
        Scene _scene;
        bool _loading;
        readonly DispatcherTimer _timer;
        string _view = "sheet";
        double _az = -35, _el = 25;
        EquipItem _sel;
        PreviewControl _preview;
        TreeView _tree;
        StackPanel _props;
        TextBlock _status, _poleInfo;
        int _tab;

        static string LastFile => Path.Combine(Plugin.DataDir, "last.json");
        static string LangFile => Path.Combine(Plugin.DataDir, "lang.txt");
        static string NamesFile => Path.Combine(Plugin.DataDir, "names.txt");
        Drag.Start _drag;
        ViewRegion _dragRegion;
        bool _dragRotate;   // Ctrl+Shift at the start = turn around the pole for the whole drag
        bool _keepStatus;
        string _dragSnapshot;
        readonly Stack<string> _undo = new Stack<string>();

        public PoleToolWindow()
        {
            try { Loc.Jp = File.Exists(LangFile) && File.ReadAllText(LangFile).Trim() == "jp"; } catch { }
            try { if (File.Exists(NamesFile) && int.TryParse(File.ReadAllText(NamesFile).Trim(), out var nm)) Loc.Names = nm; } catch { }
            try { if (File.Exists(LastFile)) _job = PoleJob.FromJson(File.ReadAllText(LastFile)); } catch { }
            _job ??= Configs.Make("pas_vct");
            _az = _job.ViewAz; _el = _job.ViewEl;
            Width = 1350; Height = 900; MinWidth = 900; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 12;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _timer.Tick += (s, e) => { _timer.Stop(); Rebuild(); };
            BuildUi();
            Rebuild();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    if (_drag != null) { CancelDrag(); e.Handled = true; }
                    else Close();
                }
                else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !(Keyboard.FocusedElement is TextBox) && _undo.Count > 0)
                {
                    var j = PoleJob.FromJson(_undo.Pop());
                    if (j != null) { _job = j; _sel = null; FillTree(); ShowProps(); Changed(); }
                    e.Handled = true;
                }
            };
        }

        public void LoadJob(PoleJob job)
        {
            _job = job; _sel = null; BuildUi(); Rebuild(); _preview.Fit();
        }

        // ================================================================== layout

        void BuildUi()
        {
            _loading = true;
            Title = Loc.T("Pole Tool");
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // --- top bar
            var top = new WrapPanel { Margin = new Thickness(6) };
            top.Children.Add(Lbl("Configuration"));
            var cfg = new ComboBox { Width = 300, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var c in Configs.List) cfg.Items.Add(new ComboBoxItem { Content = Loc.NameOf(c.jp, c.en), Tag = "b:" + c.id });
            List<string> userCfg; try { userCfg = Configs.UserNames(); } catch { userCfg = new List<string>(); }
            foreach (var n in userCfg) cfg.Items.Add(new ComboBoxItem { Content = "★ " + n, Tag = "u:" + n, FontWeight = FontWeights.SemiBold });
            cfg.SelectedIndex = 0;
            for (int k = 0; k < cfg.Items.Count; k++) if ((string)((ComboBoxItem)cfg.Items[k]).Tag == "u:" + _job.ConfigName) cfg.SelectedIndex = k;
            top.Children.Add(cfg);
            top.Children.Add(Btn("Load", () =>
            {
                var tag = (string)((ComboBoxItem)cfg.SelectedItem).Tag;
                if (MessageBox.Show(this, Loc.T("Replace the current pole with this configuration?"), Loc.T("Pole Tool"), MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                _undo.Push(_job.ToJson());
                if (tag.StartsWith("u:")) { var j = Configs.LoadUser(tag.Substring(2)); if (j == null) return; _job = j; }
                else _job = Configs.Make(tag.Substring(2), _job);
                _sel = null; _az = _job.ViewAz; _el = _job.ViewEl; BuildUi(); Changed(); _preview.Fit();
            }));
            top.Children.Add(Btn("Save as…", () =>
            {
                var name = Ask(Loc.T("Name of the configuration"), string.IsNullOrWhiteSpace(_job.ConfigName) ? _job.Pole.Designation(Loc.Jp) : _job.ConfigName);
                if (string.IsNullOrWhiteSpace(name)) return;
                if (Configs.UserExists(name) && MessageBox.Show(this, Loc.F("'{0}' exists. Overwrite?", name), Loc.T("Pole Tool"), MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                Configs.SaveUser(name, _job); _job.ConfigName = name.Trim(); BuildUi(); Changed();
                _status.Text = Loc.F("Saved configuration '{0}'.", name.Trim()); _keepStatus = true;
            }));
            top.Children.Add(Btn("Delete", () =>
            {
                var tag = (string)((ComboBoxItem)cfg.SelectedItem).Tag;
                if (!tag.StartsWith("u:")) { MessageBox.Show(this, Loc.T("Built-in configurations cannot be deleted."), Loc.T("Pole Tool")); return; }
                if (MessageBox.Show(this, Loc.F("Delete configuration '{0}'?", tag.Substring(2)), Loc.T("Pole Tool"), MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                Configs.DeleteUser(tag.Substring(2)); BuildUi();
            }));
            top.Children.Add(new Separator { Width = 20, Visibility = Visibility.Hidden });
            top.Children.Add(Lbl("Detail"));
            var det = new ComboBox { Width = 90, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var s in new[] { "Sketch", "Standard", "Detailed" }) det.Items.Add(Loc.T(s));
            det.SelectedIndex = (int)_job.Detail;
            det.SelectionChanged += (s, e) => { if (_loading) return; _job.Detail = (Detail)det.SelectedIndex; Changed(); };
            top.Children.Add(det);
            top.Children.Add(new Separator { Width = 20, Visibility = Visibility.Hidden });
            top.Children.Add(Lbl("UI language"));
            var lang = new ComboBox { Width = 90, Margin = new Thickness(4, 0, 4, 0) };
            lang.Items.Add("English"); lang.Items.Add("日本語"); lang.SelectedIndex = Loc.Jp ? 1 : 0;
            lang.SelectionChanged += (s, e) =>
            {
                if (_loading) return; Loc.Jp = lang.SelectedIndex == 1;
                try { Directory.CreateDirectory(Plugin.DataDir); File.WriteAllText(LangFile, Loc.Jp ? "jp" : "en"); } catch { }
                Dispatcher.BeginInvoke(new Action(() => { BuildUi(); Rebuild(); }));
            };
            top.Children.Add(lang);
            top.Children.Add(new Separator { Width = 20, Visibility = Visibility.Hidden });
            top.Children.Add(Lbl("Drawing text"));
            var dl = new ComboBox { Width = 130, Margin = new Thickness(4, 0, 4, 0), ToolTip = Loc.T("Language of labels, captions and the BOM in the preview and in AutoCAD") };
            dl.Items.Add("日本語"); dl.Items.Add("English"); dl.Items.Add("日本語 + English");
            dl.SelectedIndex = _job.LabelsBoth ? 2 : _job.LabelsJp ? 0 : 1;
            dl.SelectionChanged += (s, e) =>
            {
                if (_loading) return; _job.LabelsJp = dl.SelectedIndex != 1; _job.LabelsBoth = dl.SelectedIndex == 2;
                Dispatcher.BeginInvoke(new Action(() => { BuildUi(); Changed(); }));
            };
            top.Children.Add(dl);
            top.Children.Add(new Separator { Width = 20, Visibility = Visibility.Hidden });
            top.Children.Add(Lbl("List names"));
            var names = new ComboBox { Width = 130, Margin = new Thickness(4, 0, 4, 0) };
            foreach (var s in new[] { "Same as UI", "Japanese", "English", "Japanese + English" }) names.Items.Add(Loc.T(s));
            names.SelectedIndex = Loc.Names + 1;
            names.SelectionChanged += (s, e) =>
            {
                if (_loading) return; Loc.Names = names.SelectedIndex - 1;
                try { Directory.CreateDirectory(Plugin.DataDir); File.WriteAllText(NamesFile, Loc.Names.ToString(CultureInfo.InvariantCulture)); } catch { }
                Dispatcher.BeginInvoke(new Action(() => { BuildUi(); Rebuild(); }));
            };
            top.Children.Add(names);
            Grid.SetColumnSpan(top, 3); root.Children.Add(top);

            // --- left tabs
            var tabs = new TabControl { Margin = new Thickness(6, 0, 0, 0) };
            tabs.Items.Add(new TabItem { Header = Loc.T("Pole"), Content = Scroll(PoleTab()) });
            tabs.Items.Add(new TabItem { Header = Loc.T("Equipment"), Content = EquipTab() });
            tabs.Items.Add(new TabItem { Header = Loc.T("Drawing"), Content = Scroll(DrawingTab()) });
            tabs.Items.Add(new TabItem { Header = Loc.T("Library"), Content = Scroll(LibraryTab()) });
            tabs.SelectedIndex = _tab;
            tabs.SelectionChanged += (s, e) => { if (e.Source == tabs) _tab = tabs.SelectedIndex; };
            Grid.SetRow(tabs, 1); root.Children.Add(tabs);

            var split = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
            Grid.SetRow(split, 1); Grid.SetColumn(split, 1); root.Children.Add(split);

            // --- preview
            var right = new DockPanel { Margin = new Thickness(0, 0, 6, 0) };
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var (id, txt) in new[] { ("front", "Front"), ("side", "Side"), ("3d", "3D"), ("sheet", "Sheet") })
            {
                var rb = new RadioButton { Content = Loc.T(txt), GroupName = "view", IsChecked = _view == id, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                rb.Checked += (s, e) => { if (_loading) return; _view = id; Rebuild(); _preview.Fit(); };
                bar.Children.Add(rb);
            }
            bar.Children.Add(Btn("Fit", () => _preview.Fit()));
            var aMinus = Btn("A−", () => { _job.LabelPaper = Math.Max(1, Math.Round((_job.LabelPaper > 0 ? _job.LabelPaper : _job.TextPaper) - 0.5, 1)); Rebuild(); ShowLabelSize(); });
            var aPlus = Btn("A+", () => { _job.LabelPaper = Math.Min(20, Math.Round((_job.LabelPaper > 0 ? _job.LabelPaper : _job.TextPaper) + 0.5, 1)); Rebuild(); ShowLabelSize(); });
            aMinus.ToolTip = aPlus.ToolTip = Loc.T("Label text height (paper mm)");
            bar.Children.Add(aMinus); bar.Children.Add(aPlus);
            var b0 = Btn("0°", () => { _job.ElevRot = 0; Rebuild(); });
            b0.ToolTip = Loc.T("Reset the turn of the front / side views"); bar.Children.Add(b0);
            bar.Children.Add(new TextBlock { Text = Loc.T("Wheel: zoom · drag: pan (3D: orbit) · Shift+drag: turn around the pole axis (all views) · double-click: fit") + "\n" + Loc.T("Ctrl+drag an element: move it · +Shift: height only · Ctrl+Shift: turn around the pole · Alt: fine steps"), Foreground = Brushes.Gray, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(bar, Dock.Top); right.Children.Add(bar);
            _status = new TextBlock { Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(_status, Dock.Bottom); right.Children.Add(_status);
            _preview = new PreviewControl();
            _preview.Orbit += (da, de) =>
            {
                if (_view != "3d")
                {   // front / side / sheet: turn the elevation views around the pole axis
                    double r = _job.ElevRot + da;
                    double snap = Math.Round(r / 15) * 15; if (Math.Abs(r - snap) < 2) r = snap;
                    _job.ElevRot = ((r % 360) + 540) % 360 - 180;
                    _preview.Info = Loc.F("View turned {0:0}° around the pole (button 0° resets)", _job.ElevRot);
                    Rebuild(); return;
                }
                _az = ((_az + da) % 360 + 540) % 360 - 180; _el = Math.Max(-89, Math.Min(89, _el + de));
                _job.ViewAz = Math.Round(_az, 1); _job.ViewEl = Math.Round(_el, 1);
                _preview.Info = Loc.F("Azimuth {0:0}° · elevation {1:0}°", _az, _el);
                Rebuild();
            };
            _preview.DragBegin += (id, p) =>
            {
                _dragSnapshot = _job.ToJson();
                _dragRegion = _preview.Drawing?.RegionAt(p.X);
                _dragRotate = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (_dragRegion == null) { _preview.AbortDrag(); return; }
                _drag = Drag.Begin(_job, id, p - _dragRegion.Offset, _scene);
                if (_drag == null) { _preview.AbortDrag(); return; }
                _preview.Highlight = _drag.Item.Id;
                _preview.Info = Loc.T("Ctrl+drag: up/down = height, across the pole = side. Shift = height only, Ctrl+Shift = turn, Alt = fine. Esc cancels, Ctrl+Z undoes.");
            };
            _preview.DragMove += (p, mods) =>
            {
                if (_drag == null || _dragRegion == null) return;
                bool shift = mods.HasFlag(ModifierKeys.Shift), ctrl = mods.HasFlag(ModifierKeys.Control), alt = mods.HasFlag(ModifierKeys.Alt);
                _preview.Info = Drag.Apply(_job, _drag, p - _dragRegion.Offset, _dragRegion.View, alt, !_dragRotate && shift, _dragRotate);
                Rebuild();
            };
            _preview.DragEnd += () =>
            {
                if (_drag == null) return;
                var moved = _drag.Item; _drag = null;
                if (_dragSnapshot != null && _dragSnapshot != _job.ToJson()) _undo.Push(_dragSnapshot);
                _dragSnapshot = null;
                _sel = moved; FillTree(); ShowProps(); Rebuild();
            };
            right.Children.Add(new Border { BorderBrush = Brushes.Silver, BorderThickness = new Thickness(1), Child = _preview });
            Grid.SetRow(right, 1); Grid.SetColumn(right, 2); root.Children.Add(right);

            // --- bottom buttons
            var bottom = new WrapPanel { Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right };
            bottom.Children.Add(Btn("Open job…", OpenJob));
            bottom.Children.Add(Btn("Save job…", SaveJob));
            var bXl = Btn("Excel material list…", ExportExcel);
            bXl.ToolTip = Loc.T("Equipment list, fixing materials (bands, bolts, cable ties …) and cable / pipe lengths of the current pole as an .xlsx file");
            bottom.Children.Add(bXl);
            bottom.Children.Add(new Separator { Width = 24, Visibility = Visibility.Hidden });
            var bSheet = Btn("Insert sheet", () => Commands.Run("PT_INSERT2D", _job), true);
            bSheet.ToolTip = Loc.T("Views, labels, dimensions and BOM as set on the Drawing tab");
            var b2d = Btn("Insert current view as 2D", InsertCurrent2D, true);
            b2d.ToolTip = Loc.T("The view shown in the preview (front, side, turned or 3D direction) as clean lines: hidden lines removed, duplicates merged");
            var b3d = Btn("Insert current view as 3D model", () => { SetViewDir(); Commands.Run("PT_INSERT3D", _job); }, true);
            b3d.ToolTip = Loc.T("3D solids; the AutoCAD view is turned to the preview direction");
            bottom.Children.Add(bSheet); bottom.Children.Add(b2d); bottom.Children.Add(b3d);
            bottom.Children.Add(Btn("Close", Close));
            Grid.SetRow(bottom, 2); Grid.SetColumnSpan(bottom, 3); root.Children.Add(bottom);

            Content = root;
            _loading = false;
        }

        // ================================================================== Pole tab

        UIElement PoleTab()
        {
            var p = new StackPanel { Margin = new Thickness(8) };
            var presets = PolePresets.All();
            var cb = new ComboBox { MinWidth = 200 };
            foreach (var pr in presets) cb.Items.Add(pr.Name);
            int idx = presets.FindIndex(x => x.Kind == _job.Pole.Kind && Math.Abs(x.L - _job.Pole.Length) < 1 && Math.Abs(x.Top - _job.Pole.TopDia) < 0.5 && Math.Abs(x.Load - _job.Pole.DesignLoadKN) < 0.01);
            cb.SelectedIndex = idx;
            var row = new DockPanel();
            var apply = Btn("Apply", () =>
            {
                if (cb.SelectedIndex < 0) return; var pr = presets[cb.SelectedIndex];
                _job.Pole.Kind = pr.Kind; _job.Pole.Length = pr.L; _job.Pole.TopDia = pr.Top; _job.Pole.DesignLoadKN = pr.Load; _job.Pole.Taper = pr.Taper;
                _job.Pole.AutoEmbed = true; BuildUi(); Changed();
            });
            DockPanel.SetDock(apply, Dock.Right); row.Children.Add(apply); row.Children.Add(cb);
            p.Children.Add(Row("Preset", row));

            var kind = new ComboBox(); kind.Items.Add(Loc.T("Concrete pole")); kind.Items.Add(Loc.T("Steel pipe pole"));
            kind.SelectedIndex = (int)_job.Pole.Kind;
            kind.SelectionChanged += (s, e) => { if (_loading) return; _job.Pole.Kind = (PoleKind)kind.SelectedIndex; Changed(); };
            p.Children.Add(Row("Kind", kind));
            p.Children.Add(Row("Length (m)", Num(() => _job.Pole.Length / 1000, v => _job.Pole.Length = v * 1000)));
            p.Children.Add(Row("Top Ø (mm)", Num(() => _job.Pole.TopDia, v => _job.Pole.TopDia = v)));
            p.Children.Add(Row("Taper 1 :", Num(() => _job.Pole.Taper > 0 ? 1 / _job.Pole.Taper : 0, v => _job.Pole.Taper = v > 0 ? 1 / v : 0)));
            p.Children.Add(Row("Design load (kN)", Num(() => _job.Pole.DesignLoadKN, v => _job.Pole.DesignLoadKN = v)));
            var auto = new CheckBox { Content = Loc.T("Automatic embedment"), IsChecked = _job.Pole.AutoEmbed, Margin = new Thickness(0, 4, 0, 4) };
            auto.Click += (s, e) => { _job.Pole.AutoEmbed = auto.IsChecked == true; Changed(); };
            p.Children.Add(auto);
            p.Children.Add(Row("Embedment (mm)", Num(() => _job.Pole.Embed, v => { _job.Pole.Embed = v; _job.Pole.AutoEmbed = false; auto.IsChecked = false; })));
            _poleInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.DimGray };
            p.Children.Add(_poleInfo);
            p.Children.Add(new TextBlock { Text = Loc.T("Embedment follows the basic values of the technical standard interpretation (art. 59). Check soil and load conditions."), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0), FontSize = 11 });
            return p;
        }

        // ================================================================== Equipment tab

        UIElement EquipTab()
        {
            var g = new Grid { Margin = new Thickness(4) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _tree = new TreeView();
            g.Children.Add(_tree);
            var add = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var lib = new ComboBox { IsEditable = false };
            foreach (var cat in Catalog.Categories)
                foreach (var d in Catalog.All.Where(x => x.Cat == cat.id))
                    lib.Items.Add(new ComboBoxItem { Content = Loc.NameOf(cat.jp, cat.en) + " ▸ " + Loc.NameOf(d.Jp, d.En), Tag = d.Id });
            lib.SelectedIndex = 0;
            var bAdd = Btn("Add", () =>
            {
                if (lib.SelectedItem is ComboBoxItem ci) { _sel = _job.AddFromCatalog((string)ci.Tag); FillTree(); ShowProps(); Changed(); }
            });
            var bDup = Btn("Duplicate", () =>
            {
                if (_sel == null) return; var c = _sel.Clone(); c.Id = null; _job.Items.Add(c); _job.Normalize(); _sel = c; FillTree(); ShowProps(); Changed();
            });
            var bDel = Btn("Remove", () => { if (_sel == null) return; _job.Items.Remove(_sel); _sel = null; FillTree(); ShowProps(); Changed(); });
            var bArr = Btn("Arrange", () =>
            {
                _undo.Push(_job.ToJson());
                int n = Around.Arrange(_job); FillTree(); ShowProps(); Changed();
                _status.Text = Loc.F("Arranged around the pole: {0} element(s) moved to a free place.", n); _keepStatus = true;
            });
            bArr.ToolTip = Loc.T("Moves pipes, boxes and cable runs that overlap around the pole side by side (Ctrl+Z undoes)");
            foreach (var b in new[] { bArr, bDel, bDup, bAdd }) { DockPanel.SetDock(b, Dock.Right); add.Children.Add(b); }
            add.Children.Add(lib);
            Grid.SetRow(add, 1); g.Children.Add(add);
            _props = new StackPanel { Margin = new Thickness(4) };
            var sv = new ScrollViewer { Content = _props, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(sv, 2); g.Children.Add(sv);
            FillTree(); ShowProps();
            return g;
        }

        void FillTree()
        {
            _tree.Items.Clear();
            foreach (var cat in Catalog.Categories)
            {
                var items = _job.Items.Where(i => (Catalog.Find(i.Def)?.Cat ?? "user") == cat.id).ToList();
                if (items.Count == 0) continue;
                var node = new TreeViewItem { Header = Loc.NameOf(cat.jp, cat.en), IsExpanded = true, FontWeight = FontWeights.SemiBold };
                foreach (var it in items)
                {
                    var chk = new CheckBox { IsChecked = it.On, Content = ItemText(it), FontWeight = FontWeights.Normal, Tag = it };
                    chk.Click += (s, e) =>
                    {
                        var also = Links.SetOn(_job, it, chk.IsChecked == true);
                        if (also.Count > 0)
                        {
                            FillTree();
                            _status.Text = Loc.F(it.On ? "Also switched on: {0}" : "Also switched off: {0}", string.Join(", ", also.Select(ItemText)));
                            _keepStatus = true;
                        }
                        Changed();
                    };
                    var tvi = new TreeViewItem { Header = chk, Tag = it, IsSelected = it == _sel };
                    tvi.Selected += (s, e) => { if (e.OriginalSource == tvi) { _sel = it; ShowProps(); } e.Handled = true; };
                    node.Items.Add(tvi);
                }
                _tree.Items.Add(node);
            }
        }

        static string ItemText(EquipItem it)
        {
            var d = Catalog.Find(it.Def);
            string n = Loc.NameOf(string.IsNullOrEmpty(it.LabelJp) ? d?.Jp : it.LabelJp, string.IsNullOrEmpty(it.LabelEn) ? d?.En : it.LabelEn);
            n ??= it.Def;
            if (!string.IsNullOrWhiteSpace(it.Spec)) n += "  [" + it.Spec + "]";
            if (it.Qty != 1) n += "  ×" + it.Qty.ToString("0.#", CultureInfo.InvariantCulture);
            return n;
        }

        void RefreshSelText()
        {
            foreach (TreeViewItem cat in _tree.Items)
                foreach (TreeViewItem t in cat.Items)
                    if (t.Tag == _sel && t.Header is CheckBox c) c.Content = ItemText(_sel);
        }

        void ShowProps()
        {
            _props.Children.Clear();
            var it = _sel;
            if (it == null) { _props.Children.Add(new TextBlock { Text = Loc.T("Select an item in the list to edit it."), Foreground = Brushes.Gray }); return; }
            var d = Catalog.Find(it.Def);
            _props.Children.Add(new TextBlock { Text = (d == null ? it.Def : Loc.NameOf(d.Jp, d.En)) + "   (" + it.Id + ", " + (d?.Template ?? "?") + ")", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            _props.Children.Add(Row("Name (JP)", Txt(() => it.LabelJp ?? "", v => it.LabelJp = v, d?.Jp)));
            _props.Children.Add(Row("Name (EN)", Txt(() => it.LabelEn ?? "", v => it.LabelEn = v, d?.En)));
            _props.Children.Add(Row("Specification", Txt(() => it.Spec ?? "", v => it.Spec = v)));
            _props.Children.Add(Row("Remarks", Txt(() => it.Remark ?? "", v => it.Remark = v)));
            _props.Children.Add(Row("Height (mm)", Num(() => it.H, v => it.H = v)));
            var rf = new ComboBox(); rf.Items.Add(Loc.T("GL (up)")); rf.Items.Add(Loc.T("Pole top (down)")); rf.SelectedIndex = (int)it.Ref;
            rf.SelectionChanged += (s, e) =>
            {
                if (_loading) return; double z = _job.Z(it); it.Ref = (HRef)rf.SelectedIndex;
                it.H = it.Ref == HRef.Top ? _job.Pole.Top - z : z; ShowProps(); Changed();
            };
            _props.Children.Add(Row("Measured from", rf));
            var side = new ComboBox { IsEditable = true, Text = it.Side.ToString("0.#", CultureInfo.InvariantCulture) };
            foreach (var a in new[] { "0", "90", "180", "270", "45", "135", "225", "315" }) side.Items.Add(a);
            void SetSide() { if (TryNum(side.Text, out var v)) { it.Side = ((v % 360) + 360) % 360; Changed(); } }
            side.LostFocus += (s, e) => SetSide(); side.SelectionChanged += (s, e) => { if (side.SelectedItem is string str) { side.Text = str; SetSide(); } };
            side.KeyDown += (s, e) => { if (e.Key == Key.Enter) SetSide(); };
            side.ToolTip = Loc.Jp ? "0 = 正面（手前）, 90 = 右（引込線側）, 180 = 背面, 270 = 左" : "0 = front (toward viewer), 90 = right (line side), 180 = back, 270 = left";
            _props.Children.Add(Row("Direction (°)", side));
            _props.Children.Add(Row("Quantity", Num(() => it.Qty, v => it.Qty = Math.Max(0, v))));
            var col = new ComboBox { IsEditable = true };
            var colors = new (int aci, string en)[] { (0, "By layer"), (1, "Red"), (2, "Yellow"), (3, "Green"), (4, "Cyan"), (5, "Blue"), (6, "Magenta"),
                                                     (7, "White / black"), (8, "Grey"), (9, "Light grey"), (30, "Orange") };
            foreach (var c in colors) col.Items.Add(c.aci == 0 ? Loc.T(c.en) : c.aci + "  " + Loc.T(c.en));
            int ci = Array.FindIndex(colors, c => c.aci == it.Color);
            if (ci >= 0) col.SelectedIndex = ci; else col.Text = it.Color.ToString(CultureInfo.InvariantCulture);
            void SetCol()
            {
                if (_loading) return;
                int v; var txt = (col.SelectedIndex >= 0 ? colors[col.SelectedIndex].aci.ToString(CultureInfo.InvariantCulture) : col.Text ?? "").Trim().Split(' ')[0];
                if (!int.TryParse(txt, out v)) v = 0;
                it.Color = Math.Max(0, Math.Min(255, v)); Changed();
            }
            col.SelectionChanged += (s, e) => SetCol(); col.LostFocus += (s, e) => SetCol();
            col.ToolTip = Loc.Jp ? "AutoCAD の色番号 (1–255)。0 または「画層の色」で画層の色。" : "AutoCAD colour index (1–255). 0 / By layer = layer colour.";
            _props.Children.Add(Row("Colour", col));
            if (d?.AtDef != null || !string.IsNullOrEmpty(it.At))
            {
                var host = new ComboBox(); host.Items.Add(new ComboBoxItem { Content = Loc.T("(automatic)"), Tag = "" });
                foreach (var o in _job.Items.Where(x => x != it && Catalog.Find(x.Def)?.Template is "arm" or "box" or "cyl"))
                    host.Items.Add(new ComboBoxItem { Content = ItemText(o) + " (" + o.Id + ")", Tag = o.Id });
                host.SelectedIndex = 0;
                for (int k = 0; k < host.Items.Count; k++) if ((string)((ComboBoxItem)host.Items[k]).Tag == (it.At ?? "")) host.SelectedIndex = k;
                host.SelectionChanged += (s, e) => { if (_loading) return; it.At = (string)((ComboBoxItem)host.SelectedItem).Tag; Changed(); };
                _props.Children.Add(Row("Mounted on", host));
            }
            if (d?.Template is "ctrl_cable" or "meter_cable" or "earth")
            {
                var pass = new ComboBox();
                foreach (var o in new[] { "Behind boxes (between box and pole)", "In front of boxes", "Beside boxes (never on top)" }) pass.Items.Add(Loc.T(o));
                pass.SelectedIndex = (int)Math.Max(0, Math.Min(2, it.Get("pass", 0)));
                pass.SelectionChanged += (s, e) => { if (_loading) return; it.P["pass"] = pass.SelectedIndex; if (pass.SelectedIndex == 2) Around.Arrange(_job); Changed(); };
                _props.Children.Add(Row("Cable route at boxes", pass));
            }
            var hidden = new HashSet<string> { "pass", "fixedSide" };
            if (it.P != null && it.P.Keys.Any(k => !hidden.Contains(k)))
            {
                _props.Children.Add(new TextBlock { Text = Loc.T("Parameters"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
                foreach (var key in it.P.Keys.Where(k => !hidden.Contains(k)).ToList())
                {
                    var k = key;
                    var r = Row(k, Num(() => it.Get(k, 0), v => it.P[k] = v));
                    ((TextBlock)((DockPanel)r).Children[0]).Text = k + "  " + Loc.Param(k);
                    _props.Children.Add(r);
                }
            }
        }

        // ================================================================== Drawing tab

        UIElement DrawingTab()
        {
            var p = new StackPanel { Margin = new Thickness(8) };
            p.Children.Add(Head("Views"));
            p.Children.Add(Chk("Front view", () => _job.ViewFront, v => _job.ViewFront = v));
            p.Children.Add(Chk("Side view", () => _job.ViewSide, v => _job.ViewSide = v));
            p.Children.Add(Chk("Isometric view", () => _job.View3D, v => _job.View3D = v));
            p.Children.Add(Head("Labels"));
            var lm = new ComboBox(); foreach (var s in new[] { "None", "Names", "Balloons (①②)" }) lm.Items.Add(Loc.T(s));
            lm.SelectedIndex = (int)_job.Labels; lm.SelectionChanged += (s, e) => { if (_loading) return; _job.Labels = (LabelMode)lm.SelectedIndex; Changed(); };
            p.Children.Add(Row("Labels", lm));
            var ll = new ComboBox(); ll.Items.Add(Loc.T("Japanese")); ll.Items.Add(Loc.T("English")); ll.Items.Add(Loc.T("Japanese + English"));
            ll.SelectedIndex = _job.LabelsBoth ? 2 : _job.LabelsJp ? 0 : 1;
            ll.SelectionChanged += (s, e) => { if (_loading) return; _job.LabelsJp = ll.SelectedIndex != 1; _job.LabelsBoth = ll.SelectedIndex == 2; Dispatcher.BeginInvoke(new Action(() => { BuildUi(); Changed(); })); };
            p.Children.Add(Row("Label language", ll));
            p.Children.Add(Chk("Labels as AutoCAD multileaders (MLEADER)", () => _job.MLeaders, v => _job.MLeaders = v));
            p.Children.Add(Chk("Dimensions", () => _job.Dims, v => _job.Dims = v));
            p.Children.Add(Chk("BOM table", () => _job.Bom, v => _job.Bom = v));
            p.Children.Add(Chk("Ground line (GL)", () => _job.GroundLine, v => _job.GroundLine = v));
            p.Children.Add(Chk("Hide hidden lines (wipeouts)", () => _job.Masks, v => _job.Masks = v));
            p.Children.Add(Chk("Remove hidden lines + overkill (no wipeouts)", () => _job.CleanLines, v => _job.CleanLines = v));
            p.Children.Add(Head("Drawing"));
            p.Children.Add(Row("Scale 1 :", Num(() => _job.Scale, v => _job.Scale = v)));
            p.Children.Add(Row("Text height (paper mm)", Num(() => _job.TextPaper, v => _job.TextPaper = v)));
            p.Children.Add(Row("Label text height (paper mm)", Num(() => _job.LabelPaper, v => _job.LabelPaper = v)));
            var adv = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            adv.Children.Add(Row("Title / block name", Txt(() => _job.Title ?? "", v => _job.Title = v)));
            adv.Children.Add(Row("Layer prefix", Txt(() => _job.LayerPrefix ?? "", v => _job.LayerPrefix = v)));
            adv.Children.Add(Row("Wipeout layer", Txt(() => _job.WipeoutLayer ?? "", v => _job.WipeoutLayer = v)));
            adv.Children.Add(Chk("Insert as one block", () => _job.AsBlock, v => _job.AsBlock = v));
            p.Children.Add(new Expander { Header = Loc.T("Insert options (advanced)"), Content = adv, IsExpanded = false, Margin = new Thickness(0, 10, 0, 0) });
            return p;
        }

        // ================================================================== Library tab

        UIElement LibraryTab()
        {
            var p = new StackPanel { Margin = new Thickness(8) };
            var list = new ListBox { Height = 140 };
            void FillList() { list.Items.Clear(); foreach (var d in Catalog.All.Where(x => x.User)) list.Items.Add(new ListBoxItem { Content = Loc.NameOf(d.Jp, d.En) + "  (" + d.Id + ")", Tag = d }); }
            FillList();
            p.Children.Add(list);
            var jp = new TextBox(); var en = new TextBox(); var spec = new TextBox(); var unit = new TextBox { Text = "台" };
            var tpl = new ComboBox(); foreach (var t in Catalog.UserTemplates) tpl.Items.Add(t); tpl.SelectedIndex = 0;
            var cat = new ComboBox(); foreach (var c in Catalog.Categories) cat.Items.Add(new ComboBoxItem { Content = Loc.NameOf(c.jp, c.en), Tag = c.id }); cat.SelectedIndex = Catalog.Categories.Length - 1;
            var h = new TextBox { Text = "2500" }; var rf = new ComboBox(); rf.Items.Add(Loc.T("GL (up)")); rf.Items.Add(Loc.T("Pole top (down)")); rf.SelectedIndex = 0;
            var side = new TextBox { Text = "0" }; var pars = new TextBox { Text = "w=300; d=200; hgt=400" };
            List<string> blocks; try { blocks = Commands.BlockNames(); } catch { blocks = new List<string>(); }
            ComboBox Blk() { var c = new ComboBox { IsEditable = true }; c.Items.Add(""); foreach (var b in blocks) c.Items.Add(b); return c; }
            var bf = Blk(); var bs = Blk(); var b3 = Blk();
            p.Children.Add(Row("Name (JP)", jp)); p.Children.Add(Row("Name (EN)", en)); p.Children.Add(Row("Specification", spec));
            p.Children.Add(Row("Unit", unit)); p.Children.Add(Row("Template", tpl)); p.Children.Add(Row("Category", cat));
            p.Children.Add(Row("Default height (mm)", h)); p.Children.Add(Row("Measured from", rf)); p.Children.Add(Row("Direction (°)", side));
            p.Children.Add(Row("Parameters (w=300; d=200; hgt=400)", pars));
            p.Children.Add(Row("Block front view", bf)); p.Children.Add(Row("Block side view", bs)); p.Children.Add(Row("Block 3D", b3));
            EquipDef cur = null;
            list.SelectionChanged += (s, e) =>
            {
                cur = (list.SelectedItem as ListBoxItem)?.Tag as EquipDef; if (cur == null) return;
                jp.Text = cur.Jp; en.Text = cur.En; spec.Text = cur.Spec; unit.Text = cur.Unit; tpl.SelectedItem = cur.Template;
                for (int k = 0; k < cat.Items.Count; k++) if ((string)((ComboBoxItem)cat.Items[k]).Tag == cur.Cat) cat.SelectedIndex = k;
                h.Text = cur.H.ToString(CultureInfo.InvariantCulture); rf.SelectedIndex = (int)cur.Ref; side.Text = cur.Side.ToString(CultureInfo.InvariantCulture);
                pars.Text = string.Join("; ", cur.P.Select(kv => kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture)));
                bf.Text = cur.BlockFront ?? ""; bs.Text = cur.BlockSide ?? ""; b3.Text = cur.Block3D ?? "";
            };
            var btns = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            btns.Children.Add(Btn("New entry", () => { cur = null; list.SelectedIndex = -1; jp.Text = en.Text = spec.Text = ""; }));
            btns.Children.Add(Btn("Save entry", () =>
            {
                if (string.IsNullOrWhiteSpace(jp.Text) && string.IsNullOrWhiteSpace(en.Text)) return;
                if (cur == null)
                {
                    int n = 1; while (Catalog.Find("user" + n) != null) n++;
                    cur = new EquipDef { Id = "user" + n, User = true }; Catalog.All.Add(cur);
                }
                cur.Jp = string.IsNullOrWhiteSpace(jp.Text) ? en.Text : jp.Text; cur.En = string.IsNullOrWhiteSpace(en.Text) ? jp.Text : en.Text;
                cur.Spec = spec.Text; cur.Unit = string.IsNullOrWhiteSpace(unit.Text) ? "台" : unit.Text.Trim(); cur.Template = (string)tpl.SelectedItem ?? "box";
                cur.Cat = (string)((ComboBoxItem)cat.SelectedItem).Tag; cur.Ref = (HRef)Math.Max(0, rf.SelectedIndex);
                cur.H = TryNum(h.Text, out var hv) ? hv : 0; cur.Side = TryNum(side.Text, out var sv) ? sv : 0;
                cur.P = new Dictionary<string, double>();
                foreach (var part in pars.Text.Split(';', ','))
                {
                    var kv = part.Split('='); if (kv.Length == 2 && TryNum(kv[1], out var pv)) cur.P[kv[0].Trim()] = pv;
                }
                cur.BlockFront = bf.Text?.Trim(); cur.BlockSide = bs.Text?.Trim(); cur.Block3D = b3.Text?.Trim();
                try { Catalog.SaveUser(); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
                FillList(); BuildUi(); _tab = 3;
            }));
            btns.Children.Add(Btn("Delete entry", () =>
            {
                if (cur == null) return; Catalog.All.Remove(cur); cur = null;
                try { Catalog.SaveUser(); } catch { }
                FillList();
            }));
            btns.Children.Add(Btn("Add to pole", () =>
            {
                if (cur == null) return; _sel = _job.AddFromCatalog(cur.Id); _tab = 1; BuildUi(); Changed();
            }));
            p.Children.Add(btns);
            p.Children.Add(new TextBlock { Text = Loc.T("User library entries are saved to %APPDATA%\\PoleTool\\library.json. A block name, if set and present in the drawing, replaces the generated shape (block base point = centre of the equipment)."), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0), FontSize = 11 });
            return p;
        }

        // ================================================================== rebuild / preview

        void ShowLabelSize()
        {
            _status.Text = Loc.F("Label text {0:0.0} mm on paper (1:{1:0} → {2:0} mm in the model)", _job.LabelPaper, _job.Scale, _job.LabelH);
            _keepStatus = true;
        }

        void CancelDrag()
        {
            _preview.AbortDrag();
            var j = _dragSnapshot == null ? null : PoleJob.FromJson(_dragSnapshot);
            _drag = null; _dragSnapshot = null;
            if (j != null) { _job = j; _sel = null; FillTree(); ShowProps(); }
            Rebuild();
        }

        void Changed()
        {
            if (_loading) return;
            RefreshSelText();
            _timer.Stop(); _timer.Start();
        }

        void Rebuild()
        {
            try
            {
                _scene = SceneBuilder.Build(_job);
                var d = new Drawing();
                switch (_view)
                {
                    case "front": Projector.Project(_scene, FrontView, new P2(0, 0), d, _job.Masks); Ground(d); break;
                    case "side": Projector.Project(_scene, SideView, new P2(0, 0), d, _job.Masks); Ground(d); break;
                    case "3d": Projector.Project(_scene, new View3(_az, _el), new P2(0, 0), d, _job.Masks); break;
                    default: d = Sheet.Compose(_job, _scene, out _); break;
                }
                _preview.OrbitMode = _view == "3d";
                _preview.CanDrag = id => Drag.Target(_job, id) != null;
                if (d.Views.Count == 0)
                    d.Views.Add(new ViewRegion { X0 = double.MinValue / 4, X1 = double.MaxValue / 4, Offset = new P2(0, 0),
                        View = _view == "front" ? FrontView : _view == "side" ? SideView : new View3(_az, _el) });
                _preview.Drawing = d;
                var warn = new List<string>(_scene.Warnings);
                if (_drag == null)
                    try
                    {
                        foreach (var (a, b) in Around.Overlaps(_job, _scene).Take(4))
                            warn.Add(Loc.F("Overlap around the pole: {0} ↔ {1}", ItemText(_job.Item(a) ?? new EquipItem { Def = a }), ItemText(_job.Item(b) ?? new EquipItem { Def = b })));
                    }
                    catch { }
                if (_keepStatus) { _keepStatus = false; if (warn.Count > 0) _status.Text += "   " + string.Join("   ", warn); }
                else _status.Text = string.Join("   ", warn);
                if (_poleInfo != null)
                    _poleInfo.Text = _job.Pole.Designation(Loc.Jp) + "\n" + Loc.F("Pole top {0} mm above GL · Ø at GL {1:0} mm · embedment {2:0} mm",
                        _job.Pole.Top.ToString("0", CultureInfo.InvariantCulture), _job.Pole.DiaAt(0), _job.Pole.EmbedUsed);
                if (_drag == null) try { Directory.CreateDirectory(Plugin.DataDir); File.WriteAllText(LastFile, _job.ToJson(true)); } catch { }
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        /// <summary>Copies the direction of the current preview into the job (ViewAz / ViewEl).</summary>
        void SetViewDir()
        {
            var v = _view == "3d" ? new View3(_az, _el) : _view == "side" ? SideView : FrontView;
            _job.ViewAz = v.Az; _job.ViewEl = v.El;
        }

        void InsertCurrent2D()
        {
            if (_view == "sheet") { Commands.Run("PT_INSERT2D", _job); return; }   // the sheet is the current view
            SetViewDir();
            Commands.Run("PT_INSERTVIEW", _job);
        }

        View3 FrontView => Math.Abs(_job.ElevRot) < 1e-9 ? View3.Front : new View3(_job.ElevRot, 0);
        View3 SideView => Math.Abs(_job.ElevRot) < 1e-9 ? View3.Side : new View3(_job.ElevRot - 90, 0);

        void Ground(Drawing d)
        {
            d.Bounds(out var x0, out _, out var x1, out _);
            d.E.Add(new DLine { Layer = Lyr.Ground, Pts = new List<P2> { new P2(x0 - 300, 0), new P2(x1 + 300, 0) } });
        }

        void ExportExcel()
        {
            string name = string.IsNullOrWhiteSpace(_job.ConfigName) ? (string.IsNullOrWhiteSpace(_job.Title) ? "pole" : _job.Title) : _job.ConfigName;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var dlg = new SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = name + "_" + Loc.T("materials") + ".xlsx" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                Materials.WriteExcel(_job, _scene ?? SceneBuilder.Build(_job), dlg.FileName);
                if (MessageBox.Show(this, Loc.F("Saved: {0}\nOpen it now?", dlg.FileName), Loc.T("Pole Tool"), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("Pole Tool")); }
        }

        void SaveJob()
        {
            var dlg = new SaveFileDialog { Filter = "Pole job (*.polejob.json)|*.polejob.json|JSON|*.json", FileName = string.IsNullOrWhiteSpace(_job.Title) ? "pole" : _job.Title };
            if (dlg.ShowDialog(this) == true) File.WriteAllText(dlg.FileName, _job.ToJson(true));
        }

        void OpenJob()
        {
            var dlg = new OpenFileDialog { Filter = "Pole job (*.polejob.json;*.json)|*.polejob.json;*.json" };
            if (dlg.ShowDialog(this) != true) return;
            var j = PoleJob.FromJson(File.ReadAllText(dlg.FileName));
            if (j == null) { MessageBox.Show(this, "Invalid file."); return; }
            LoadJob(j);
        }

        // ================================================================== small UI helpers

        /// <summary>Small text input dialog.</summary>
        string Ask(string prompt, string initial)
        {
            var w = new Window { Title = Loc.T("Pole Tool"), Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var p = new StackPanel { Margin = new Thickness(12) };
            p.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });
            var tb = new TextBox { Text = initial ?? "", Width = 320 }; p.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 70, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true, MinWidth = 70 };
            ok.Click += (s, e) => { w.DialogResult = true; };
            row.Children.Add(ok); row.Children.Add(cancel); p.Children.Add(row);
            w.Content = p; w.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            return w.ShowDialog() == true ? tb.Text : null;
        }

        static ScrollViewer Scroll(UIElement e) => new ScrollViewer { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        static TextBlock Lbl(string en) => new TextBlock { Text = Loc.T(en), VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Head(string en) => new TextBlock { Text = Loc.T(en), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };

        Button Btn(string en, Action a, bool strong = false)
        {
            var b = new Button { Content = Loc.T(en), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(3, 0, 3, 0), MinWidth = 60 };
            if (strong) b.FontWeight = FontWeights.SemiBold;
            b.Click += (s, e) => { try { a(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("Pole Tool")); } };
            return b;
        }

        static UIElement Row(string en, UIElement ctl)
        {
            var dp = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var t = new TextBlock { Text = Loc.T(en), Width = 150, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(t, Dock.Left); dp.Children.Add(t); dp.Children.Add(ctl);
            return dp;
        }

        static bool TryNum(string s, out double v) =>
            double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);

        TextBox Num(Func<double> get, Action<double> set)
        {
            var tb = new TextBox { Text = get().ToString("0.###", CultureInfo.InvariantCulture) };
            void Commit()
            {
                if (TryNum(tb.Text, out var v)) { set(v); tb.ClearValue(TextBox.BackgroundProperty); Changed(); }
                else tb.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xE0));
            }
            tb.LostFocus += (s, e) => Commit();
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) Commit(); };
            tb.PreviewMouseWheel += (s, e) =>
            {
                if (!tb.IsKeyboardFocusWithin || !TryNum(tb.Text, out var v)) return;
                double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 100 : Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1 : 10;
                if (Math.Abs(v) < 20 && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 0.1 : 1;
                v += e.Delta > 0 ? step : -step; tb.Text = v.ToString("0.###", CultureInfo.InvariantCulture); Commit(); e.Handled = true;
            };
            return tb;
        }

        TextBox Txt(Func<string> get, Action<string> set, string placeholder = null)
        {
            var tb = new TextBox { Text = get() };
            if (!string.IsNullOrEmpty(placeholder)) tb.ToolTip = placeholder;
            tb.TextChanged += (s, e) => { if (_loading) return; set(tb.Text); Changed(); };
            return tb;
        }

        CheckBox Chk(string en, Func<bool> get, Action<bool> set)
        {
            var c = new CheckBox { Content = Loc.T(en), IsChecked = get(), Margin = new Thickness(0, 3, 0, 3) };
            c.Click += (s, e) => { set(c.IsChecked == true); Changed(); };
            return c;
        }
    }
}
