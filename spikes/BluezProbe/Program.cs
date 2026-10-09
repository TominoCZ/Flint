// SPIKE. Drives the Spark 2 from Linux through the app's own BlueZLink, AmpSession and Supervisor, and
// prints every chunk both ways. Run from the repo root, with the phone's Bluetooth off. Everything printed also goes
// to research/captures/probe-<stamp>.log (gitignored), and research/captures/probe.log links to the newest. Usage:
//   BluezProbe <address> read [seconds]   read-only: the connect handshake's gets and all 8 slots, then listen
//   BluezProbe <address> watch            the supervisor until Ctrl+C: power-cycle the amp, toggle the PC's Bluetooth, suspend
using BluezProbe;
using Flint.Core.Logging;

if (args.Length < 2 || args[1] is not ("read" or "watch"))
{
    Console.WriteLine("usage: BluezProbe <address> read [seconds] | watch");
    return 2;
}

var captures = Directory.CreateDirectory(Path.Combine("research", "captures")).FullName;
var logPath = Path.Combine(captures, $"probe-{DateTime.Now:yyyyMMdd-HHmmss}.log");
using var tee = new TeeWriter(Console.Out, new StreamWriter(logPath) { AutoFlush = true });
Console.SetOut(tee);
var latest = Path.Combine(captures, "probe.log");
File.Delete(latest);
File.CreateSymbolicLink(latest, Path.GetFileName(logPath));
Console.WriteLine($"logging to {logPath}");

Log.Start(echo: true);
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

var probe = new Probe(args[0]);
return args[1] switch
{
    "read" => await probe.ReadAsync(TimeSpan.FromSeconds(args.Length > 2 ? int.Parse(args[2]) : 5), stop.Token),
    _ => await probe.WatchAsync(stop.Token),
};
