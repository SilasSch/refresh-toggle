namespace RefreshToggle.Core;

internal enum ErrorCode
{
    Unknown,
    Validation,
    NotFound,
    Unavailable,
    Conflict,
    Persistence,
    Registration,
    DisplayQuery,
    UnsupportedRefreshRate,
    DisplayApply,
    Installation,
    Security,
    Cancelled
}

internal sealed record AppError(ErrorCode Code, string Message, Exception? Exception = null)
{
    public static AppError Validation(string message) => new(ErrorCode.Validation, message);

    public static AppError Cancelled(string message = "The operation was cancelled.") =>
        new(ErrorCode.Cancelled, message);

    public static AppError Combine(AppError primary, params AppError?[] additional)
    {
        ArgumentNullException.ThrowIfNull(primary);

        var messages = additional
            .Where(error => error is not null)
            .Select(error => error!.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        return messages.Length == 0
            ? primary
            : primary with { Message = $"{primary.Message} Rollback details: {string.Join(" ", messages)}" };
    }
}
