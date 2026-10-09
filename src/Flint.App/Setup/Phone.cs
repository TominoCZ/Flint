namespace Flint.App.Setup;

/// <summary>What <c>adb devices -l</c> says about the phones on USB, as one state setup can explain.</summary>
internal enum PhoneState
{
    /// <summary>No phone, or one without USB debugging on (adb sees nothing either way).</summary>
    None,

    /// <summary>USB debugging is on, but the phone is waiting for the user to allow this computer.</summary>
    Unauthorized,

    /// <summary>Linux will not let adb open the phone's USB device: no udev rule for it.</summary>
    NoPermissions,

    /// <summary>adb sees the phone but cannot talk to it.</summary>
    Offline,

    /// <summary>More than one phone is connected.</summary>
    Several,

    Ready,
}

/// <param name="Serial">The ready phone's serial, for adb's -s.</param>
/// <param name="Model">The ready phone's model, as it calls itself ("Pixel 7").</param>
internal sealed record Phone(PhoneState State, string? Serial = null, string? Model = null)
{
    /// <summary>The phones in <c>adb devices -l</c>'s output, as one state.</summary>
    public static Phone Parse(string devicesOutput)
    {
        var devices = devicesOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('*') && !l.StartsWith("List of devices"))
            .Select(l => l.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) // a tab or spaces
            .Where(f => f.Length == 2)
            .ToList();
        if (devices.Count == 0) return new Phone(PhoneState.None);
        if (devices.Count > 1) return new Phone(PhoneState.Several);
        var (serial, rest) = (devices[0][0], devices[0][1]);
        if (rest.StartsWith("unauthorized")) return new Phone(PhoneState.Unauthorized);
        if (rest.StartsWith("no permissions")) return new Phone(PhoneState.NoPermissions);
        if (!rest.StartsWith("device")) return new Phone(PhoneState.Offline);
        var model = rest.Split((char[]?)null).FirstOrDefault(f => f.StartsWith("model:"))?["model:".Length..].Replace('_', ' ');
        return new Phone(PhoneState.Ready, serial, model);
    }

    /// <summary>The command that installs the udev rules adb needs on this distribution, or null for one not known here.</summary>
    public static string? UdevCommand(string osRelease)
    {
        var ids = Java.OsIds(osRelease);
        if (ids.Overlaps(["debian", "ubuntu"])) return "sudo apt install android-sdk-platform-tools-common";
        if (ids.Overlaps(["fedora", "rhel", "centos"])) return "sudo dnf install android-tools";
        if (ids.Overlaps(["arch"])) return "sudo pacman -S android-udev";
        return null;
    }
}
