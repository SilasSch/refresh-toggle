using RefreshToggle.Application;

namespace RefreshToggle.Tests;

public sealed class TrailingDebouncerTests
{
    [Fact]
    public async Task OnlyLatestSignalRunsAfterExactDelay()
    {
        var scheduler = new ManualDelayScheduler();
        await using var debouncer = new TrailingDebouncer(TimeSpan.FromMilliseconds(500), scheduler);
        var calls = new List<int>();

        var first = debouncer.SignalAsync(_ =>
        {
            calls.Add(1);
            return ValueTask.CompletedTask;
        });
        var second = debouncer.SignalAsync(_ =>
        {
            calls.Add(2);
            return ValueTask.CompletedTask;
        });

        Assert.All(scheduler.Requests, request => Assert.Equal(TimeSpan.FromMilliseconds(500), request.Delay));
        scheduler.ReleaseAll();
        await Task.WhenAll(first, second);

        Assert.Equal([2], calls);
    }

    [Fact]
    public async Task EventStormStillRunsFinalActionExactlyOnce()
    {
        var scheduler = new ManualDelayScheduler();
        await using var debouncer = new TrailingDebouncer(TimeSpan.FromMilliseconds(500), scheduler);
        var calls = new List<int>();
        var tasks = new List<Task>();

        for (var index = 0; index < 100; index++)
        {
            var captured = index;
            tasks.Add(debouncer.SignalAsync(_ =>
            {
                calls.Add(captured);
                return ValueTask.CompletedTask;
            }));
        }

        scheduler.ReleaseAll();
        await Task.WhenAll(tasks);

        Assert.Equal([99], calls);
    }

    [Fact]
    public async Task DisposeCancelsPendingActionAndRejectsNewSignals()
    {
        var scheduler = new ManualDelayScheduler();
        var debouncer = new TrailingDebouncer(TimeSpan.FromMilliseconds(500), scheduler);
        var calls = 0;
        var pending = debouncer.SignalAsync(_ =>
        {
            calls++;
            return ValueTask.CompletedTask;
        });

        await debouncer.DisposeAsync();
        await pending;

        Assert.Equal(0, calls);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = debouncer.SignalAsync(_ => ValueTask.CompletedTask);
        });
    }

    [Fact]
    public void ConstructorRejectsNonPositiveDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrailingDebouncer(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrailingDebouncer(TimeSpan.FromTicks(-1)));
    }
}
