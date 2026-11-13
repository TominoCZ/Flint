using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Flint.App.Controls;

/// <summary>
/// An image's shape filled in one colour, laid under the image to hide whatever is drawn behind it. Pixels at
/// <see cref="Solid"/> alpha or more (the "not found" picture's translucent amps are 15 %) and transparent holes the
/// shape encloses are filled solid; fainter ones fade in proportion, so a soft drop shadow stays soft instead of
/// turning into a block.
/// </summary>
internal static class Silhouette
{
    private const int Solid = 38;

    public static WriteableBitmap? Of(Bitmap source, Color fill)
    {
        if (source.Format?.BitsPerPixel != 32) return null;
        var (w, h) = (source.PixelSize.Width, source.PixelSize.Height);
        var pixels = new byte[w * h * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { source.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length, w * 4); }
        finally { handle.Free(); }

        // Flood the outside from the border through pixels with no alpha (byte 3 in both RGBA and BGRA).
        var outside = new bool[w * h];
        var stack = new Stack<int>();
        for (var x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
        for (var y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
        while (stack.TryPop(out var i))
        {
            if (outside[i] || pixels[i * 4 + 3] != 0) continue;
            outside[i] = true;
            int x = i % w, y = i / w;
            if (x > 0) stack.Push(i - 1);
            if (x < w - 1) stack.Push(i + 1);
            if (y > 0) stack.Push(i - w);
            if (y < h - 1) stack.Push(i + w);
        }

        var result = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        var row = new int[w];
        using var locked = result.Lock();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                var alpha = outside[i] ? 0 : pixels[i * 4 + 3] is 0 ? 255 : Math.Min(255, pixels[i * 4 + 3] * 255 / Solid);
                row[x] = alpha << 24 | fill.R * alpha / 255 << 16 | fill.G * alpha / 255 << 8 | fill.B * alpha / 255; // premultiplied
            }
            Marshal.Copy(row, 0, locked.Address + y * locked.RowBytes, w);
        }
        return result;
    }
}
