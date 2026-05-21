namespace AI_Discord_Bot.Models;

public class AppSettings
{
    public string BotToken { get; set; } = "";

    public List<string> MonitoredChannelIds { get; set; } = [];

    public string ReportChannelId { get; set; } = "";

    public string ReportDmUserId { get; set; } = "";

    public string ModelPath { get; set; } = "";

    public int GpuLayerCount { get; set; } = 25;

    public string BackendMode { get; set; } = "Automatic";

    public bool FlashAttention { get; set; } = true;

    public int AnalysisIntervalSeconds { get; set; } = 30;

    public int WindowSize { get; set; } = 50;

    public bool AutoContextSize { get; set; } = true;

    public int ContextSize { get; set; } = 4096;

    public float Temperature { get; set; } = 0.6f;

    public List<string> EnabledReportTypes { get; set; } =
    [
        "ToxicChat",
        "Complaints",
        "ServerIssues",
        "ServerDown",
        "SimSpeedIssues",
        "PlayerComplaints"
    ];

    public string CustomRules { get; set; } = DefaultBotRules.Text;
}
