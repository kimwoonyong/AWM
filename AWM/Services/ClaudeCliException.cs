namespace AWM.Services;

public enum ClaudeCliFailure
{
    NotInstalled,
    Failed,
    TimedOut,
    BadOutput,
}

/// <summary>
/// 메시지는 그대로 화면 상태 줄에 나간다.
/// </summary>
public sealed class ClaudeCliException(ClaudeCliFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ClaudeCliFailure Failure { get; } = failure;
}
