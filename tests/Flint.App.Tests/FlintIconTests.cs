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

    // Windows shows an exe's icon from an .ico built into it (Flint.App.csproj's ApplicationIcon), so Flint.ico is this
    // drawing at the sizes Explorer and the taskbar ask for, each a PNG. Written by this test when FLINT_WRITE_ICON=1.
    [Fact]
    public Task The_exe_icon_is_this_drawing() => headless.Run(async () =>
    {
        var ico = Ico([16, 24, 32, 48, 64, 256]);
        var path = Path.Combine(Repo(), "src", "Flint.App", "Flint.ico");
        if (Environment.GetEnvironmentVariable("FLINT_WRITE_ICON") == "1") File.WriteAllBytes(path, ico);
        Assert.True(File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(ico),
                    "Flint.ico is not the drawing in FlintIcon: run this test with FLINT_WRITE_ICON=1 to rewrite it");
        await Task.CompletedTask;
    });

    /// <summary>An .ico of PNG images: a 6-byte header, a 16-byte entry per image, then the images.</summary>
    private static byte[] Ico(int[] sizes)
    {
        var images = sizes.Select(size =>
        {
            using var icon = FlintIcon.Render(size);
            using var png = new MemoryStream();
            icon.Save(png, new PngBitmapEncoderOptions());
            return png.ToArray();
        }).ToList();
        using var ico = new MemoryStream();
        using var w = new BinaryWriter(ico);
        w.Write((short)0); // reserved
        w.Write((short)1); // an icon, not a cursor
        w.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] % 256)); // 256 is written as 0
            w.Write((byte)(sizes[i] % 256));
            w.Write((byte)0); // no palette
            w.Write((byte)0);
            w.Write((short)1); // planes
            w.Write((short)32); // bits per pixel
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images) w.Write(image);
        return ico.ToArray();
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Flint.slnx"))) dir = dir.Parent!;
        return dir.FullName;
    }
}
