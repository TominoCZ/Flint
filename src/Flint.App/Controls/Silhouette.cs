using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Flint.App.Controls;

/// <summary>
/// An image's outline filled solid in one colour: every pixel not reachable from the edge through fully transparent
/// pixels, so see-through parts inside the shape (the amps' translucent grilles) are filled too. Laid under the image,
/// it hides whatever is drawn behind it.
/// </summary>
internal static class Silhouette
{
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
        var solid = (int)(0xFF000000u | (uint)(fill.R << 16 | fill.G << 8 | fill.B));
        var row = new int[w];
        using var locked = result.Lock();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++) row[x] = outside[y * w + x] ? 0 : solid;
            Marshal.Copy(row, 0, locked.Address + y * locked.RowBytes, w);
        }
        return result;
    }
}
