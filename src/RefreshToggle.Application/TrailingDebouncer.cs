namespace RefreshToggle.Application;

internal interface IDelayScheduler
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemDelayScheduler : IDelayScheduler
{
    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        new(Task.Delay(delay, cancellationToken));
}

internal sealed class TrailingDebouncer : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly TimeSpan _delay;
    private readonly IDelayScheduler _scheduler;
    private CancellationTokenSource? _pendingCancellation;
    private Task _pendingTask = Task.CompletedTask;
    private bool _disposed;

    public TrailingDebouncer(TimeSpan delay, IDelayScheduler? scheduler = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);

        _delay = delay;
        _scheduler = scheduler ?? new SystemDelayScheduler();
    }

    public Task SignalAsync(Func<CancellationToken, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _pendingCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _pendingCancellation = cancellation;
            _pendingTask = RunAsync(cancellation, action);
            return _pendingTask;
        }
    }

    private async Task RunAsync(
        CancellationTokenSource cancellation,
        Func<CancellationToken, ValueTask> action)
    {
        try
        {
            await _scheduler.DelayAsync(_delay, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            await action(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Superseded signals complete normally; only the trailing signal runs.
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pendingCancellation, cancellation))
                {
                    _pendingCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task pendingTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingCancellation?.Cancel();
            pendingTask = _pendingTask;
        }

        await pendingTask.ConfigureAwait(false);
    }
}
