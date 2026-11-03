using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace QuickNotchCs;

/// <summary>
/// Готовит фон раскрытого острова. Размытие и затемнение «запекаются» в одну картинку один раз
/// при смене настроек, поэтому во время анимации раскрытия они ничего не стоят.
/// </summary>
public static class IslandBackground
{
    // Размер хоста раскрытого острова (MainWindow.xaml: ExpandedHost 864x532)
    private const double W = 864, H = 532;

    public static ImageBrush? Build(string path, double dim, double blur)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;       // файл не остаётся занятым
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = 1400;                      // огромные фото не едят память
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();

        // Запас по краям, чтобы размытие не давало прозрачную кайму
        double pad = blur > 0 ? blur * 2 : 0;

        var image = new Image
        {
            Source = bmp,
            Stretch = Stretch.UniformToFill,
            Width = W + pad * 2,
            Height = H + pad * 2,
            Margin = new Thickness(-pad),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (blur > 0)
        {
            image.Effect = new BlurEffect
            {
                Radius = blur,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Quality,
            };
        }
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        var root = new Grid { Width = W, Height = H, ClipToBounds = true, Background = Brushes.Black };
        root.Children.Add(image);
        if (dim > 0)
        {
            root.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(dim, 0, 0.95)), 0, 0, 0)),
            });
        }

        var size = new Size(W, H);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap((int)W, (int)H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        rtb.Freeze();

        var brush = new ImageBrush(rtb)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Top,
        };
        brush.Freeze();
        return brush;
    }
}
