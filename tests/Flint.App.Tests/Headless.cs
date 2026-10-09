using Avalonia;
using Avalonia.Headless;

namespace Flint.App.Tests;

/// <summary>
/// The one headless Avalonia these tests share (a process can host only one), with the app's own theme, rendering
/// through Skia so frames can be captured. Test classes join it with <c>[Collection(Headless.Name)]</c>.
/// </summary>
public sealed class Headless : IDisposable
{
    public const string Name = "headless";

    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(Headless));

    /// <summary>
    /// Runs an async test body on the UI thread to its end, failures included. A plain <c>Session.Dispatch(async () => …)</c>
    /// binds to <c>Dispatch&lt;Task&gt;(Func&lt;Task&gt;)</c> and returns at the body's first real await, so the rest of
    /// the body, and any failure in it, was lost; the overload for a body with a result pumps until it completes.
    /// </summary>
    public Task Run(Func<Task> body) => Session.Dispatch(async () =>
    {
        await body();
        return true;
    }, CancellationToken.None);

    // Looked up by name, as Avalonia's own entry points are.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition(Headless.Name)]
public sealed class HeadlessCollection : ICollectionFixture<Headless>;
