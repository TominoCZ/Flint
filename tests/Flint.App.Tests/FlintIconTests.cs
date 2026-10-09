using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xunit.Abstractions;

namespace Flint.App.Tests;

// Flint's own icon, drawn in code: a combo amp, gold control panel on top, opaque grille below, transparent corners, at the
// sizes the launcher and the tray use. Saved beside the test binary (icon-*.png) for a look.
[Collection(Headless.Name)]
public sealed class FlintIconTests(Headless headless, ITestOutputHelper output)
{
    [Fact]
    public Task The_icon_is_an_amp_with_a_gold_panel_and_round_corners() => headless.Run(async () =>
    {
        foreach (var size in new[] { 256, 22 })
        {
            using var icon = FlintIcon.Render(size);
            var file = Path.Combine(AppContext.BaseDirectory, $"icon-{size}.png");
            icon.Save(file, new PngBitmapEncoderOptions());
            output.WriteLine($"rendered to {file}");
            Assert.Equal(new PixelSize(size, size), icon.PixelSize);
            using var pixels = new WriteableBitmap(icon.PixelSize, icon.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = pixels.Lock()) icon.CopyPixels(new PixelRect(icon.PixelSize), buffer.Address, buffer.RowBytes * size, buffer.RowBytes);
            using (var buffer = pixels.Lock())
            {
                int Pixel(int x, int y) => System.Runtime.InteropServices.Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
                Assert.Equal(0, Pixel(0, 0) >>> 24); // a rounded corner: nothing there
                Assert.Equal(255, Pixel(size / 2, (int)(size * 0.64)) >>> 24); // the grille
                var panel = Pixel((int)(size * 0.45), (int)(size * 0.25)); // between two knobs
                Assert.True(((panel >> 16) & 0xff) > 0xb0 && (panel & 0xff) < 0xa0, $"the panel should be gold, not {panel:x8}");
            }
        }
        await Task.CompletedTask;
    });
}
