using RefreshToggle.App;

namespace RefreshToggle.Tests;

/// <summary>
/// Tests for the rewrite baseline bootstrap output.
/// </summary>
public class AppBootstrapTests
{
    /// <summary>
    /// Ensures the baseline project returns the expected startup message.
    /// </summary>
    [Fact]
    public void GetWelcomeMessageReturnsRewriteBaselineMessage()
    {
        Assert.Equal("RefreshToggle rewrite baseline is ready.", AppBootstrap.GetWelcomeMessage());
    }
}
