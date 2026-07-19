using RefreshToggle.Application;

namespace RefreshToggle.Tests;

public sealed class SerializedCommandGateTests
{
    [Fact]
    public async Task ConcurrentCommandsExecuteOneAtATimeInArrivalOrder()
    {
        await using var gate = new SerializedCommandGate();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();
        var active = 0;
        var maximumActive = 0;

        async Task Run(int id, bool block)
        {
            await gate.ExecuteAsync(async cancellationToken =>
            {
                var current = Interlocked.Increment(ref active);
                maximumActive = Math.Max(maximumActive, current);
                order.Add(id);
                if (block)
                {
                    firstEntered.SetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }

                Interlocked.Decrement(ref active);
            });
        }

        var first = Run(1, true);
        await firstEntered.Task;
        var second = Run(2, false);
        var third = Run(3, false);

        Assert.Equal([1], order);
        releaseFirst.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.Equal([1, 2, 3], order);
        Assert.Equal(1, maximumActive);
    }

    [Fact]
    public async Task CancelledQueuedCommandNeverExecutes()
    {
        await using var gate = new SerializedCommandGate();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ranSecond = false;

        var first = gate.ExecuteAsync(async cancellationToken =>
        {
            firstEntered.SetResult();
            await releaseFirst.Task.WaitAsync(cancellationToken);
        });
        await firstEntered.Task;

        using var cancellation = new CancellationTokenSource();
        var second = gate.ExecuteAsync(
            _ =>
            {
                ranSecond = true;
                return Task.CompletedTask;
            },
            cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(ranSecond);
        releaseFirst.SetResult();
        await first;
    }

    [Fact]
    public async Task DisposedGateRejectsCommands()
    {
        var gate = new SerializedCommandGate();
        await gate.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            gate.ExecuteAsync(_ => Task.CompletedTask));
    }
}
