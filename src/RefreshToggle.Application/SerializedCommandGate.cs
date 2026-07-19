namespace RefreshToggle.Application;

internal sealed class SerializedCommandGate : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;

    public Task ExecuteAsync(
        Func<CancellationToken, Task> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteAsync(
            async token =>
            {
                await command(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);

        var entered = false;
        try
        {
            await _semaphore.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            entered = true;
            return await command(linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            if (entered)
            {
                _semaphore.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        await _semaphore.WaitAsync().ConfigureAwait(false);
        _semaphore.Release();
        _shutdown.Dispose();
        _semaphore.Dispose();
    }
}
