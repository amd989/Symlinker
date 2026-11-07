using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>
/// Opens Symlinker's main window in a given UI language with the form filled in, renders it at
/// 2.5x and places it under a headline on a 1920x1080 Store screenshot.
/// Args: out.png headline culture [Symlinker launch options...]
/// </summary>
internal static class Program
{
    private const double Scale = 2.5;
    private const int Width = 1920;
    private const int Height = 1080;

    [STAThread]
    private static void Main(string[] args)
    {
        var outPath = args[0];
        var headline = args[1];
        var culture = new CultureInfo(args[2]);
        var appArgs = args.Skip(3).ToArray();
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // LaunchOptions and the MainWindow constructor are internal; reflection keeps the app untouched.
        var asm = typeof(Symlinker.App).Assembly;
        var app = new Symlinker.App();
        app.InitializeComponent();
        var options = asm.GetType("Symlinker.LaunchOptions")
            .GetMethod("Parse", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(null, new object[] { appArgs });
        var window = (Window)Activator.CreateInstance(asm.GetType("Symlinker.MainWindow"),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { options }, null);

        // No hover states in the screenshot, wherever the mouse is.
        window.IsHitTestVisible = false;
        window.ContentRendered += async (_, _) =>
        {
            await Task.Delay(2000);
            // The permission notice depends on this PC (Developer Mode, admin), so leave it out.
            foreach (var name in new[] { "adminPanel", "createLinkShield" })
            {
                if (window.FindName(name) is UIElement element)
                {
                    element.Visibility = Visibility.Collapsed;
                }
            }

            window.UpdateLayout();
            await Task.Delay(300);
            Save(Compose(RenderWindow(window), headline), outPath);
            app.Shutdown();
        };
        app.Run(window);
    }

    private static BitmapSource RenderWindow(Window window)
    {
        var root = (Visual)VisualTreeHelper.GetChild(window, 0);
        var bitmap = new RenderTargetBitmap(
            (int)(window.ActualWidth * Scale), (int)(window.ActualHeight * Scale),
            96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    private static BitmapSource Compose(BitmapSource shot, string headline)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(Color.FromRgb(0x1a, 0x1d, 0x22), Color.FromRgb(0x24, 0x33, 0x1c), 45);
            dc.DrawRectangle(background, null, new Rect(0, 0, Width, Height));

            var typeface = new Typeface(
                new FontFamily("Segoe UI Variable Display, Segoe UI, Nirmala UI, Yu Gothic UI, Microsoft YaHei UI"),
                FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var text = new FormattedText(headline, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, 54, Brushes.White, 1.0)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = Width - 160,
            };
            dc.DrawText(text, new Point(80, 90));

            // Pixel size, not Width/Height: those are in DIPs and undo the 2.5x scale.
            double w = shot.PixelWidth;
            double h = shot.PixelHeight;
            var x = (Width - w) / 2;
            var y = 230 + ((Height - 230 - h) / 2) - 20;

            // Soft shadow under the window.
            for (var i = 1; i <= 14; i++)
            {
                var alpha = (byte)(10 - (i * 0.6));
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)), null,
                    new Rect(x - (i * 2), y - (i * 2) + 12, w + (i * 4), h + (i * 4)), 12 + (i * 2), 12 + (i * 2));
            }

            dc.DrawImage(shot, new Rect(x, y, w, h));
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1),
                new Rect(x - 0.5, y - 0.5, w + 1, h + 1));
        }

        var result = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        return result;
    }

    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
