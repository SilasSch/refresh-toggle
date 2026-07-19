using RefreshToggle.Application;
using RefreshToggle.Core;

namespace RefreshToggle.Tests;

public sealed class CoreUtilityTests
{
    [Fact]
    public void FailedGenericResultThrowsWhenValueIsRead()
    {
        var result = Result.Failure<string>(new AppError(ErrorCode.Unknown, "failed"));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public async Task SystemSchedulerCompletesDelay()
    {
        var scheduler = new SystemDelayScheduler();

        await scheduler.DelayAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);
    }

    [Fact]
    public async Task DuplicateDebouncerDisposeIsSafe()
    {
        var debouncer = new TrailingDebouncer(TimeSpan.FromMilliseconds(1));

        await debouncer.DisposeAsync();
        await debouncer.DisposeAsync();
    }
}
