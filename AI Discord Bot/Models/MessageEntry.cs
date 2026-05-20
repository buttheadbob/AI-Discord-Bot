namespace AI_Discord_Bot.Models;

public class MessageEntry
{
    public string MessageId { get; set; } = "";

    public string Content { get; set; } = "";

    public string AuthorName { get; set; } = "";

    public string AuthorId { get; set; } = "";

    public string ChannelName { get; set; } = "";

    public string ChannelId { get; set; } = "";

    public string GuildId { get; set; } = "";

    public DateTimeOffset Timestamp { get; set; }
}
