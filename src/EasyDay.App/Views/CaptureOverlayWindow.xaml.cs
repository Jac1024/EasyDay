using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyDay.App.Infrastructure;
using Point = System.Windows.Point;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Size = System.Drawing.Size;

namespace EasyDay.App.Views;

public partial class CaptureOverlayWindow : Window
{
    private Point? _start;
    private Point _current;
    private bool _capturing;
    private DpiScale _dpi = new(1, 1);

    public CaptureOverlayWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            _dpi = VisualTreeHelper.GetDpi(this);
            var (x, y, width, height) = ScreenUtil.VirtualScreen();
            Left = x / _dpi.DpiScaleX;
            Top = y / _dpi.DpiScaleY;
            Width = width / _dpi.DpiScaleX;
            Height = height / _dpi.DpiScaleY;
            Activate();
            Focus();
        };
    }

    public byte[]? Result { get; private set; }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root);
        _current = _start.Value;
        _capturing = true;
        SelRect.Visibility = Visibility.Visible;
        UpdateRect(_start.Value, _current);
        Root.CaptureMouse();
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_capturing || _start is null)
        {
            return;
        }

        _current = e.GetPosition(Root);
        UpdateRect(_start.Value, _current);
    }

    private async void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_capturing || _start is null)
        {
            return;
        }

        _capturing = false;
        Root.ReleaseMouseCapture();
        _current = e.GetPosition(Root);

        var rect = new Rect(_start.Value, _current);
        if (rect.Width < 8 || rect.Height < 8)
        {
            Close();
            return;
        }

        Hide();
        await Task.Delay(140); // 等遮罩真正从屏幕消失，避免把半透明层截进去

        try
        {
            Result = Capture(rect);
        }
        catch (Exception ex)
        {
            FileLog.Error("截图失败", ex);
            MessageBox.Show("截图失败：" + ex.Message, "EasyDay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Close();
    }

    private void UpdateRect(Point start, Point current)
    {
        var rect = new Rect(start, current);
        Canvas.SetLeft(SelRect, rect.X);
        Canvas.SetTop(SelRect, rect.Y);
        SelRect.Width = rect.Width;
        SelRect.Height = rect.Height;
    }

    private byte[] Capture(Rect rect)
    {
        var (virtualX, virtualY, _, _) = ScreenUtil.VirtualScreen();

        var x = virtualX + (int)Math.Round(rect.X * _dpi.DpiScaleX);
        var y = virtualY + (int)Math.Round(rect.Y * _dpi.DpiScaleY);
        var width = Math.Max(1, (int)Math.Round(rect.Width * _dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(rect.Height * _dpi.DpiScaleY));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
