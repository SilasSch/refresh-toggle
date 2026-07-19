namespace RefreshToggle.Windows.Deployment;

internal static class CleanupWaitHandle
{
    public static Task<bool> WaitOneAsync(
        WaitHandle waitHandle,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(waitHandle);

        if (waitHandle.WaitOne(TimeSpan.Zero))
        {
            return Task.FromResult(true);
        }

        if (timeout <= TimeSpan.Zero)
        {
            return Task.FromResult(false);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisteredWaitHandle? registration = null;
        CancellationTokenRegistration cancellationRegistration = default;

        registration = ThreadPool.RegisterWaitForSingleObject(
            waitHandle,
            (_, timedOut) => completion.TrySetResult(!timedOut),
            state: null,
            timeout,
            executeOnlyOnce: true);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationRegistration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
        }

        return AwaitAndDisposeAsync(completion.Task, registration, cancellationRegistration);
    }

    private static async Task<bool> AwaitAndDisposeAsync(
        Task<bool> task,
        RegisteredWaitHandle registration,
        CancellationTokenRegistration cancellationRegistration)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            registration.Unregister(waitObject: null);
            cancellationRegistration.Dispose();
        }
    }
}
