using System.Text;
using System.Text.Json;

namespace AI_Discord_Bot.Services;

public class AnalysisService
{
    private readonly MessageBufferService _buffer;
    private readonly LlamaService _llama;
    private readonly DiscordBotService _discord;
    private readonly List<string> _enabledReportTypes;
    private string _customRules;
    private readonly List<RecentReport> _recentReports = [];

    public string CustomRules
    {
        get => _customRules;
        set => _customRules = value ?? "";
    }
    private const int MaxReportHistory = 20;

    public event Action<string>? LogMessage;

    public AnalysisService(
        MessageBufferService buffer,
        LlamaService llama,
        DiscordBotService discord,
        List<string> enabledReportTypes,
        string customRules)
    {
        _buffer = buffer;
        _llama = llama;
        _discord = discord;
        _enabledReportTypes = enabledReportTypes;
        _customRules = customRules;
    }

    public async Task ProcessAllChannelsAsync(string reportChannelId, string reportDmUserId)
    {
        var startTime = DateTimeOffset.UtcNow;
        var channelIds = _buffer.GetDirtyChannels();

        if (channelIds.Count == 0)
        {
            LogMessage?.Invoke("Cycle: No new messages to process.");
            return;
        }

        var totalMessages = 0;
        var reportsSent = 0;

        LogMessage?.Invoke($"Cycle start: {channelIds.Count} channel(s) with new messages, {_buffer.TotalMessageCount} total buffered");

        foreach (var channelId in channelIds)
        {
            var messages = _buffer.GetWindow(channelId);
            if (messages.Count == 0) continue;

            totalMessages += messages.Count;

            var channelName = messages[0].ChannelName;
            var userPrompt = BuildUserPrompt(messages);
            var systemPrompt = BuildSystemPrompt();

            try
            {
                var response = await _llama.PromptAsync(systemPrompt, userPrompt);
                var rawSample = response.Length > 300 ? response[..300] + "..." : response;
                LogMessage?.Invoke($"#{channelName}: LLM response ({response.Length} chars): {rawSample.Replace("\n", "\\n")}");

                var result = ParseResponse(response);
                LogMessage?.Invoke($"#{channelName}: Parsed → needsReport={result.NeedsReport}" +
                                  (result.NeedsReport ? $", type={result.ReportType}, severity={result.Severity}" : ""));

                if (result.NeedsReport)
                {
                    var title = $"Report: {result.Title ?? result.ReportType ?? "Issue"} [{result.Severity?.ToUpper() ?? "UNKNOWN"}]";
                    var description = BuildReportDescription(result, channelName, messages);
                    if (description.Length > 4000)
                        description = description[..3997] + "...";

                    if (!string.IsNullOrWhiteSpace(reportChannelId))
                    {
                        try { await _discord.SendReportAsync(reportChannelId, title, description); }
                        catch (Exception ex) { LogMessage?.Invoke($"#{channelName}: Failed to send report to channel — {ex.Message}"); }
                    }

                    if (!string.IsNullOrWhiteSpace(reportDmUserId))
                    {
                        try { await _discord.SendDmAsync(reportDmUserId, title, description); }
                        catch (Exception ex) { LogMessage?.Invoke($"#{channelName}: Failed to send DM report — {ex.Message}"); }
                    }

                    if (string.IsNullOrWhiteSpace(reportChannelId) && string.IsNullOrWhiteSpace(reportDmUserId))
                        LogMessage?.Invoke($"#{channelName}: Report generated but no output target configured");

                    reportsSent++;
                    RecordReport(channelName, result);
                    LogMessage?.Invoke($"#{channelName}: REPORT SENT [{result.Severity?.ToUpper()}] {result.Title ?? result.ReportType}");
                }
                else
                {
                    LogMessage?.Invoke($"#{channelName}: No issues found");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"#{channelName}: Error — {ex.Message}");
            }

            _buffer.MarkClean(channelId);
        }

        var elapsed = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
        LogMessage?.Invoke($"Cycle complete in {elapsed:F1}s — {reportsSent} report(s) from {totalMessages} message(s)");
    }

    private string BuildSystemPrompt()
    {
        var reportTypesStr = string.Join(", ", _enabledReportTypes.Select(FormatReportType));

        var jsonExample = @"{""needsReport"":true,""reportType"":""type"",""severity"":""low|medium|high|critical"",""title"":""brief title"",""summary"":""brief summary"",""details"":""detailed analysis"",""involvedUsers"":[""user1"",""user2""]}";
        var jsonNoReport = @"{""needsReport"":false}";

        return $"""
                You are a Discord server moderation assistant. Analyze chat messages from ONE channel.

                Monitor for these types of issues:
                {reportTypesStr}

                {BuildRecentReportsSection()}
                Respond ONLY with a JSON object. No other text. No markdown. No explanations.

                If issues are found, respond with:
                {jsonExample}

                If no issues are found, respond with:
                {jsonNoReport}

                Rules:
                {_customRules}
                """;
    }

    private string BuildRecentReportsSection()
    {
        if (_recentReports.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("Recent reports already filed (do NOT re-report these):");
        foreach (var report in _recentReports)
        {
            var ago = FormatTimeAgo(report.Timestamp);
            sb.AppendLine($"- [{report.ReportType}] \"{report.Title}\" in #{report.ChannelName} ({ago}): {report.Summary}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string FormatTimeAgo(DateTimeOffset timestamp)
    {
        var ago = DateTimeOffset.UtcNow - timestamp;
        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }

    private static string BuildUserPrompt(List<Models.MessageEntry> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Messages from this channel (sliding window):");
        sb.AppendLine();

        foreach (var msg in messages.OrderBy(m => m.Timestamp))
        {
            var time = msg.Timestamp.ToLocalTime().ToString("HH:mm");
            sb.AppendLine($"[#{msg.ChannelName}] {msg.AuthorName} ({time}): {msg.Content}");
        }

        return sb.ToString();
    }

    private static string FormatReportType(string type) => type switch
    {
        "ToxicChat" => "- Toxic Chat / Behavior: Offensive language, harassment, bullying, hate speech",
        "Complaints" => "- Complaints: User complaints about the server, staff, or other users",
        "ServerIssues" => "- Server Issues: Technical problems with the server (lag, crashes, bugs)",
        "ServerDown" => "- Server Down: Reports that the server is offline or inaccessible",
        "SimSpeedIssues" => "- SimSpeed Issues: Problems with simulation speed or tick rate",
        "PlayerComplaints" => "- Player Complaints: Complaints about other players' behavior",
        _ => $"- {type}"
    };

    private ReportResult ParseResponse(string response)
    {
        var json = ExtractJson(response);

        if (string.IsNullOrWhiteSpace(json))
        {
            LogMessage?.Invoke("Parse: no JSON found in response");
            return new ReportResult { NeedsReport = false };
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new ReportResult
            {
                NeedsReport = root.TryGetProperty("needsReport", out var nr) && nr.GetBoolean(),
                ReportType = root.TryGetProperty("reportType", out var rt) ? rt.GetString() : null,
                Severity = root.TryGetProperty("severity", out var sev) ? sev.GetString() : "unknown",
                Title = root.TryGetProperty("title", out var t) ? t.GetString() : null,
                Summary = root.TryGetProperty("summary", out var s) ? s.GetString() : null,
                Details = root.TryGetProperty("details", out var d) ? d.GetString() : null,
                InvolvedUsers = root.TryGetProperty("involvedUsers", out var users)
                    ? users.EnumerateArray().Select(u => u.GetString() ?? "").ToList()
                    : []
            };
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Parse: JSON deserialization failed — {ex.Message}");
            return new ReportResult { NeedsReport = false };
        }
    }

    private static string? ExtractJson(string response)
    {
        var trimmed = response.Trim();

        var start = trimmed.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        for (var i = start; i < trimmed.Length; i++)
        {
            switch (trimmed[i])
            {
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0)
                        return trimmed[start..(i + 1)];
                    break;
                default: continue;
            }
        }

        return null;
    }

    private static string BuildReportDescription(ReportResult result, string channelName, List<Models.MessageEntry> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Channel**: #{channelName}");
        sb.AppendLine($"**Type**: {result.ReportType ?? "Unknown"}");
        sb.AppendLine($"**Severity**: {result.Severity?.ToUpper() ?? "Unknown"}");

        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            sb.AppendLine();
            sb.AppendLine($"**Summary**: {result.Summary}");
        }

        if (!string.IsNullOrWhiteSpace(result.Details))
        {
            sb.AppendLine();
            sb.AppendLine($"**Details**: {result.Details}");
        }

        if (result.InvolvedUsers is { Count: > 0 })
        {
            sb.AppendLine();
            var mentions = string.Join(" ", result.InvolvedUsers.Select(name =>
            {
                var match = messages.FirstOrDefault(m =>
                    m.AuthorName.Equals(name, StringComparison.Ordinal) ||
                    m.AuthorName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match is not null && !string.IsNullOrWhiteSpace(match.AuthorId))
                    return $"<@{match.AuthorId}>";
                return $"@{name}";
            }));
            sb.AppendLine($"**Involved Users**: {mentions}");
        }

        if (messages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**__Messages Reviewed__**:");
            var ordered = messages.OrderBy(m => m.Timestamp).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var m = ordered[i];
                var time = m.Timestamp.ToLocalTime().ToString("HH:mm");
                var link = BuildMessageLink(m);
                var linkText = string.IsNullOrWhiteSpace(link) ? "" : $" → {link}";
                var mention = !string.IsNullOrWhiteSpace(m.AuthorId)
                    ? $"<@{m.AuthorId}>"
                    : $"@{m.AuthorName}";
                sb.AppendLine($"{i + 1}. {mention} ({time}): {Truncate(m.Content, 120)}{linkText}");
            }
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }

    private static string BuildMessageLink(Models.MessageEntry m)
    {
        if (string.IsNullOrWhiteSpace(m.GuildId))
            return $"https://discord.com/channels/@me/{m.ChannelId}/{m.MessageId}";

        return $"https://discord.com/channels/{m.GuildId}/{m.ChannelId}/{m.MessageId}";
    }

    private void RecordReport(string channelName, ReportResult result)
    {
        _recentReports.Add(new RecentReport(
            channelName,
            result.ReportType ?? "Unknown",
            result.Title ?? "Untitled",
            result.Summary ?? "",
            DateTimeOffset.UtcNow));

        while (_recentReports.Count > MaxReportHistory)
            _recentReports.RemoveAt(0);
    }
}

internal class ReportResult
{
    public bool NeedsReport { get; set; }
    public string? ReportType { get; set; }
    public string? Severity { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Details { get; set; }
    public List<string> InvolvedUsers { get; set; } = [];
}

internal sealed record RecentReport(
    string ChannelName,
    string ReportType,
    string Title,
    string Summary,
    DateTimeOffset Timestamp
);
