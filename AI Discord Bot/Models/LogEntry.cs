namespace AI_Discord_Bot.Models;

public class LogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; } = "";
}
