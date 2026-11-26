using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PoleToolApp.UI
{
    /// <summary>Adds a "Pole Tool" ribbon tab.</summary>
    public static class RibbonUi
    {
        const string TabId = "PT_POLETOOL_TAB";

        public static void Create()
        {
            if (ComponentManager.Ribbon != null) Build();
            else ComponentManager.ItemInitialized += OnItemInitialized;
        }

        static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            Build();
        }

        static void Build()
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon.FindTab(TabId) != null) return;
                var tab = new RibbonTab { Title = "Pole Tool", Id = TabId };
                var src = new RibbonPanelSource { Title = "Pole Tool" };
                src.Items.Add(Button("Pole Tool\n装柱図", "POLETOOL", "Build a pole with equipment / 装柱図を作成", "P"));
                src.Items.Add(Button("Edit Pole\n編集", "POLEEDIT", "Edit a placed pole / 配置済みの装柱図を編集", "E"));
                tab.Panels.Add(new RibbonPanel { Source = src });
                ribbon.Tabs.Add(tab);
            }
            catch { /* ribbon is optional */ }
        }

        static RibbonButton Button(string text, string command, string tip, string letter) => new RibbonButton
        {
            Text = text, Id = "PT_" + command, ShowText = true, ShowImage = true,
            Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical,
            LargeImage = Icon(letter, 32), Image = Icon(letter, 16),
            ToolTip = tip, CommandParameter = command, CommandHandler = new Handler()
        };

        static ImageSource Icon(string letter, int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double c = size / 2.0;
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2F, 0x8F, 0x5B)), null, new Rect(size * 0.40, size * 0.05, size * 0.20, size * 0.9));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2F, 0x8F, 0x5B)), null, new Rect(size * 0.12, size * 0.18, size * 0.76, size * 0.10));
                var ft = new FormattedText(letter, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size * 0.45, Brushes.White, 1.0);
                dc.DrawText(ft, new Point(c - ft.Width / 2, size * 0.55 - ft.Height / 2));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv); bmp.Freeze();
            return bmp;
        }

        sealed class Handler : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter)
            {
                if (parameter is RibbonButton b && b.CommandParameter is string cmd)
                    AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("_" + cmd + " ", true, false, true);
            }
        }
    }
}
