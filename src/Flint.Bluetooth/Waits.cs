namespace Flint.Bluetooth;

/// <summary>
/// Waiting on a Bluetooth call with a deadline. <see cref="Task.WaitAsync(CancellationToken)"/> stops waiting but leaves
/// the call running, and when the stack fails it later (BlueZ's <c>br-connection-canceled</c> after a Cancel), nobody
/// observes the failure and it surfaces as an unobserved task exception. Here the call's failure is observed whatever happens.
/// </summary>
internal static class Waits
{
    public static Task Until(this Task call, CancellationToken cancellationToken) => Observed(call).WaitAsync(cancellationToken);

    public static Task<T> Until<T>(this Task<T> call, CancellationToken cancellationToken) => Observed(call).WaitAsync(cancellationToken);

    public static Task Until(this Task call, TimeSpan timeout) => Observed(call).WaitAsync(timeout);

    private static T Observed<T>(T call) where T : Task
    {
        call.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return call;
    }
}
