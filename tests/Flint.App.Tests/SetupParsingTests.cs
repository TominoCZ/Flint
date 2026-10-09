using Flint.App.Setup;

namespace Flint.App.Tests;

// What setup reads from the tools it runs: adb's device list, java's version, jadx's progress, and which commands to
// show for the user's distribution.
public sealed class SetupParsingTests
{
    private const string Ubuntu = "PRETTY_NAME=\"Ubuntu 24.04 LTS\"\nID=ubuntu\nID_LIKE=debian\n";
    private const string Mint = "NAME=\"Linux Mint\"\nID=linuxmint\nID_LIKE=\"ubuntu debian\"\n";
    private const string Fedora = "NAME=\"Fedora Linux\"\nID=fedora\n";
    private const string Manjaro = "ID=manjaro\nID_LIKE=arch\n";
    private const string Gentoo = "ID=gentoo\n";

    [Fact]
    public void Each_adb_device_state_becomes_one_phone_state()
    {
        Assert.Equal(PhoneState.None, Phone.Parse("List of devices attached\n\n").State);
        Assert.Equal(PhoneState.None, Phone.Parse("* daemon not running; starting now at tcp:5037\n* daemon started successfully\nList of devices attached\n").State);
        Assert.Equal(PhoneState.Unauthorized, Phone.Parse("List of devices attached\nR58M12ABCDE            unauthorized usb:1-2 transport_id:3\n").State);
        Assert.Equal(PhoneState.NoPermissions, Phone.Parse(
            "List of devices attached\nR58M12ABCDE            no permissions (missing udev rules? user is in the plugdev group); see [http://developer.android.com/tools/device.html] usb:1-2\n").State);
        Assert.Equal(PhoneState.Offline, Phone.Parse("List of devices attached\nR58M12ABCDE\toffline\n").State);
        Assert.Equal(PhoneState.Several, Phone.Parse("List of devices attached\nAAA device usb:1-1 model:X\nBBB device usb:1-2 model:Y\n").State);

        var ready = Phone.Parse("List of devices attached\nR58M12ABCDE            device usb:1-2 product:beyond1ltexx model:SM_G973F device:beyond1 transport_id:4\n");
        Assert.Equal(new Phone(PhoneState.Ready, "R58M12ABCDE", "SM G973F"), ready);
        Assert.Equal(new Phone(PhoneState.Ready, "R58M12ABCDE", null), Phone.Parse("List of devices attached\nR58M12ABCDE\tdevice\n")); // without -l: a tab
    }

    [Fact]
    public void Java_versions_are_read_in_both_numberings()
    {
        Assert.Equal(21, Java.Major("openjdk version \"21.0.12.1\" 2026-08-18\nOpenJDK Runtime Environment (build 21.0.12.1+1)"));
        Assert.Equal(17, Java.Major("openjdk version \"17\" 2021-09-14"));
        Assert.Equal(8, Java.Major("java version \"1.8.0_392\"\nJava(TM) SE Runtime Environment"));
        Assert.Null(Java.Major("bash: java: command not found"));
    }

    [Fact]
    public void Install_commands_follow_the_distribution_and_the_ones_it_is_like()
    {
        Assert.Equal("sudo apt install openjdk-17-jre-headless", Java.InstallCommand(Ubuntu));
        Assert.Equal("sudo apt install openjdk-17-jre-headless", Java.InstallCommand(Mint));
        Assert.Equal("sudo dnf install java-21-openjdk-headless", Java.InstallCommand(Fedora));
        Assert.Equal("sudo pacman -S jre21-openjdk-headless", Java.InstallCommand(Manjaro));
        Assert.Null(Java.InstallCommand(Gentoo));
        Assert.Null(Java.InstallCommand(""));

        Assert.Equal("sudo apt install android-sdk-platform-tools-common", Phone.UdevCommand(Mint));
        Assert.Equal("sudo pacman -S android-udev", Phone.UdevCommand(Manjaro));
        Assert.Null(Phone.UdevCommand(Gentoo));
    }

    [Fact]
    public void Jadx_progress_lines_are_read_and_others_ignored()
    {
        Assert.Equal((3144, 17803), Jadx.Progress("INFO  - progress: 3144 of 17803 (17%)"));
        Assert.Null(Jadx.Progress("INFO  - processing ..."));
        Assert.Null(Jadx.Progress("ERROR - finished with errors, count: 156"));
    }

    [Fact]
    public void Jadx_sources_with_windows_line_endings_read_the_same()
    {
        // jadx ends lines as the OS does; the noise gate's one entry is read line by line, so \r\n once lost it on Windows.
        var root = Directory.CreateTempSubdirectory("flint-crlf-").FullName;
        try
        {
            var model = Directory.CreateDirectory(Path.Combine(root, "com", "positivegrid", "spark", "model")).FullName;
            File.WriteAllText(Path.Combine(model, "FxNoiseGate.java"),
                "public enum FxNoiseGate implements d {\r\n" +
                "    NoiseGate(\"bias.noisegate\", \"NoiseGate\", R.drawable.module_icon_ng_25_noisegate, a.f48867a.g());\r\n\r\n" +
                "    private final String dspID;\r\n}\r\n");
            var sources = new JavaSources(root);
            var errors = new List<string>();
            var entry = Assert.Single(ModelEnums.Entries("FxNoiseGate", sources, new ResourceNames(sources), errors));
            Assert.Empty(errors);
            Assert.Equal("bias.noisegate", (string?)entry["dspId"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
