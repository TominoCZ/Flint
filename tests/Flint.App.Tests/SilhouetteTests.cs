using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Flint.App.Controls;

namespace Flint.App.Tests;

// The connect screen's rings must not show through the amp picture's see-through grille: the picture's silhouette,
// drawn under it, is solid inside its outline (holes included) and clear outside it.
[Collection(Headless.Name)]
public sealed class SilhouetteTests(Headless headless)
{
    [Fact]
    public Task Fills_inside_the_outline_and_leaves_the_outside_clear() => headless.Run(() =>
    {
        // 5×5: transparent border, a faint 3×3 ring, a fully transparent hole in its middle.
        var source = new WriteableBitmap(new PixelSize(5, 5), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var locked = source.Lock())
            for (var y = 1; y <= 3; y++)
                for (var x = 1; x <= 3; x++)
                    if ((x, y) != (2, 2)) Marshal.WriteInt32(locked.Address + y * locked.RowBytes + x * 4, 0x26262626);

        var silhouette = Silhouette.Of(source, Color.Parse("#101010"))!;
        var pixels = new int[25];
        using (var locked = silhouette.Lock()) Marshal.Copy(locked.Address, pixels, 0, 25);

        Assert.Equal(unchecked((int)0xFF101010), pixels[2 * 5 + 2]); // the hole
        Assert.Equal(unchecked((int)0xFF101010), pixels[1 * 5 + 1]); // the ring
        Assert.Equal(0, pixels[0]);                                   // outside
        Assert.Equal(0, pixels[4 * 5 + 3]);
        return Task.CompletedTask;
    });
}
