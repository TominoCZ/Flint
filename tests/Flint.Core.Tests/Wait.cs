namespace Flint.Core.Tests;

public static class Wait
{
    /// <summary>Polls until the condition holds, failing the test after 5 s.</summary>
    public static async Task Until(Func<bool> condition)
    {
        for (var end = DateTime.UtcNow + TimeSpan.FromSeconds(5); !condition(); await Task.Delay(5))
            Assert.True(DateTime.UtcNow < end, "condition not reached within 5 s");
    }
}
