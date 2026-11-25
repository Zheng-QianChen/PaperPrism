using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EInkSmartInvert.UI;

public static class AppIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static byte[]? _cachedIcoBytes;

    private static byte[]? GetEmbeddedIcoBytes()
    {
        if (_cachedIcoBytes != null)
            return _cachedIcoBytes;

        var asm = Assembly.GetExecutingAssembly();
        using Stream? stream = asm.GetManifestResourceStream("EInkSmartInvert.Assets.app.ico");
        if (stream == null)
            return null;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _cachedIcoBytes = ms.ToArray();
        return _cachedIcoBytes;
    }

    public static Icon CreateWindowIcon(int preferredSize = 64)
    {
        byte[]? bytes = GetEmbeddedIcoBytes();
        if (bytes != null)
        {
            using var ms = new MemoryStream(bytes);
            return new Icon(ms, new Size(preferredSize, preferredSize));
        }
        return CreateFallbackIcon(preferredSize, active: true);
    }

    public static Bitmap CreateHeaderLogoBitmap(int sizePx)
    {
        byte[]? bytes = GetEmbeddedIcoBytes();
        if (bytes != null)
        {
            using var ms = new MemoryStream(bytes);
            using var ico = new Icon(ms, new Size(256, 256));
            using var rawBmp = ico.ToBitmap();
            var scaled = new Bitmap(sizePx, sizePx, PixelFormat.Format32bppArgb);
            using var g = System.Drawing.Graphics.FromImage(scaled);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(rawBmp, new Rectangle(0, 0, sizePx, sizePx));
            return scaled;
        }

        using var fallback = CreateFallbackIcon(sizePx, active: true);
        return fallback.ToBitmap();
    }

    public static Icon CreateTrayIcon(bool active, int sizePx = 32)
    {
        byte[]? bytes = GetEmbeddedIcoBytes();
        if (bytes == null)
            return CreateFallbackIcon(sizePx, active);

        using var ms = new MemoryStream(bytes);
        if (active)
            return new Icon(ms, new Size(sizePx, sizePx));

        // Paused state: render dimmed desaturated version with a subtle pause badge
        using var activeIco = new Icon(ms, new Size(256, 256));
        using var srcBmp = activeIco.ToBitmap();
        using var dimBmp = new Bitmap(sizePx, sizePx, PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(dimBmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var cm = new ColorMatrix(new float[][]
            {
                new float[] { 0.30f, 0.30f, 0.30f, 0f, 0f },
                new float[] { 0.50f, 0.50f, 0.50f, 0f, 0f },
                new float[] { 0.15f, 0.15f, 0.15f, 0f, 0f },
                new float[] { 0f,    0f,    0f,    0.65f, 0f },
                new float[] { 0.08f, 0.08f, 0.08f, 0f, 1f }
            });
            using var ia = new ImageAttributes();
            ia.SetColorMatrix(cm);
            g.DrawImage(srcBmp, new Rectangle(0, 0, sizePx, sizePx), 0, 0, srcBmp.Width, srcBmp.Height, GraphicsUnit.Pixel, ia);
        }

        IntPtr hIcon = dimBmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Icon CreateFallbackIcon(int sizePx, bool active)
    {
        using var bmp = new Bitmap(sizePx, sizePx, PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            int pad = Math.Max(1, sizePx / 12);
            var rect = new Rectangle(pad, pad, sizePx - pad * 2, sizePx - pad * 2);
            using var darkBrush = new SolidBrush(active ? Color.FromArgb(24, 30, 41) : Color.Gray);
            using var lightBrush = new SolidBrush(active ? Color.FromArgb(248, 250, 252) : Color.LightGray);
            using var accentPen = new Pen(active ? Color.FromArgb(37, 99, 235) : Color.DarkGray, Math.Max(1.5f, sizePx / 14f));
            g.FillPie(darkBrush, rect, 135, 180);
            g.FillPie(lightBrush, rect, 315, 180);
            g.DrawEllipse(accentPen, rect);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
