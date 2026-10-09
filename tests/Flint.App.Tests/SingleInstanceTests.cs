namespace Flint.App.Tests;

// A second launch must not connect to the amp alongside the first: it shows the first one's window and exits.
public sealed class SingleInstanceTests
{
    [Fact]
    public async Task A_second_claim_shows_the_first_instances_window_and_a_claim_after_exit_succeeds()
    {
        var folder = Directory.CreateTempSubdirectory("flint-").FullName;
        try
        {
            var first = SingleInstance.Claim(folder);
            Assert.NotNull(first);
            var activated = new TaskCompletionSource();
            first.Activated += () => activated.TrySetResult();

            Assert.Null(SingleInstance.Claim(folder));
            await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));

            first.Dispose();
            using var next = SingleInstance.Claim(folder);
            Assert.NotNull(next);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
