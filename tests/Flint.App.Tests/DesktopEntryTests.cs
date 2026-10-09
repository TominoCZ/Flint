namespace Flint.App.Tests;

// The launcher and autostart entries must start the app from any path (Desktop Entry spec, "The Exec key").
public sealed class DesktopEntryTests
{
    [Fact]
    public void Exec_quotes_only_what_needs_it_and_escapes_as_the_spec_says()
    {
        var text = DesktopEntry.Text(["/home/me/.dotnet/dotnet", "/home/me/My Apps/100% $park\\Flint.dll"], "/a b/icon.png");

        Assert.Contains("\nExec=/home/me/.dotnet/dotnet \"/home/me/My Apps/100%% \\\\$park\\\\\\\\Flint.dll\"\n", text);
        Assert.Contains("\nIcon=/a b/icon.png\n", text);
        Assert.Contains($"\nStartupWMClass={DesktopEntry.WmClass}\n", text);
        Assert.StartsWith("[Desktop Entry]\n", text);
        Assert.DoesNotContain("Icon=", DesktopEntry.Text(["/x"], null));
    }

    // The same command starts the app again after setup, and, with --minimized added, at login (into the tray).
    [Fact]
    public void The_app_is_started_as_itself_or_as_dotnet_with_its_dll_and_only_login_starts_it_minimized()
    {
        Assert.Equal(["/home/me/.local/bin/Flint"], DesktopEntry.CommandOf("/home/me/.local/bin/Flint", "/home/me/.local/bin/"));
        Assert.Equal(["/usr/bin/dotnet", "/repo/bin/Release/net10.0/Flint.dll"], DesktopEntry.CommandOf("/usr/bin/dotnet", "/repo/bin/Release/net10.0/"));
        Assert.Equal([.. DesktopEntry.Command, Autostart.Minimized], Autostart.Command);
        Assert.DoesNotContain(Autostart.Minimized, DesktopEntry.Command);
    }
}
