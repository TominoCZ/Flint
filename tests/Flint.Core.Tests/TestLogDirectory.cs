using System.Runtime.CompilerServices;

namespace Flint.Core.Tests;

internal static class TestLogDirectory
{
    // Code under test logs through the static Log; keep that out of the owner's real log folder.
    [ModuleInitializer]
    internal static void Redirect() =>
        Environment.SetEnvironmentVariable("FLINT_LOG_DIR", Path.Combine(Path.GetTempPath(), "flint-test-logs"));
}
