using System.Globalization;
using AI_Discord_Bot.Models;
using SimpleDiscordNet;
using SimpleDiscordNet.Context;
using SimpleDiscordNet.Entities;
using SimpleDiscordNet.Events;
using SimpleDiscordNet.Logging;
using SimpleDiscordNet.Models;

namespace AI_Discord_Bot.Services;

public class DiscordBotService
{
    private readonly MessageBufferService _buffer;
    private HashSet<string> _monitoredChannelIds;
    private DiscordBot? _bot;

    public event Action? StatusChanged;
    public event Action<Exception>? ErrorOccurred;

    public bool IsConnected => _bot is not null;
    public string BotName { get; private set; } = "";

    public DiscordBotService(MessageBufferService buffer, HashSet<string> monitoredChannelIds)
    {
        _buffer = buffer;
        _monitoredChannelIds = monitoredChannelIds;
    }

    public void UpdateMonitoredChannels(HashSet<string> channelIds)
    {
        _monitoredChannelIds = channelIds;
    }

    public async Task ConnectAsync(string token)
    {
        _bot = DiscordBot.NewBuilder()
            .WithToken(token)
            .WithIntents(DiscordIntents.Guilds | DiscordIntents.GuildMessages | DiscordIntents.MessageContent | DiscordIntents.GuildMembers)
            .WithSynchronizationContext(SynchronizationContext.Current)
            .WithPreloadOnStart(guilds: true, channels: true, members: false)
            .Build();

        DiscordEvents.MessageCreated += OnMessageCreated;
        DiscordEvents.Log += OnLog;

        await _bot.StartAsync();
        BotName = "Connected";
        StatusChanged?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        if (_bot is null) return;

        DiscordEvents.MessageCreated -= OnMessageCreated;
        DiscordEvents.Log -= OnLog;

        await _bot.StopAsync();
        _bot = null;
        BotName = "";
        StatusChanged?.Invoke();
    }

    public List<DiscordGuild> GetGuilds()
    {
        return DiscordContext.Guilds.ToList();
    }

    public async Task<IEnumerable<DiscordChannel>> GetTextChannelsAsync(string guildId)
    {
        if (_bot is null) return [];
        var channels = await _bot.GetGuildChannelsAsync(guildId);
        return channels.Where(c => c.IsTextChannel);
    }

    public async Task<List<DiscordChannel>> GetAllTextChannelsAsync()
    {
        if (_bot is null) return [];
        var guilds = DiscordContext.Guilds;
        var channels = new List<DiscordChannel>();

        foreach (var guild in guilds)
        {
            var guildChannels = await _bot.GetGuildChannelsAsync(guild.Id);
            channels.AddRange(guildChannels.Where(c => c.IsTextChannel));
        }

        return channels;
    }

    public async Task SendReportAsync(string channelId, string title, string description)
    {
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(DiscordColor.Red)
            .WithTimestamp(DateTimeOffset.UtcNow);

        await DiscordContext.Operations.SendMessageAsync(channelId, "", embed);
    }

    public async Task SendDmAsync(string userId, string title, string description)
    {
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(DiscordColor.Red)
            .WithTimestamp(DateTimeOffset.UtcNow);

        await DiscordContext.Operations.SendDMAsync(userId, "", embed);
    }

    public async Task<DiscordChannel?> GetChannelAsync(string channelId)
    {
        if (_bot is null) return null;
        return await _bot.GetChannelAsync(channelId);
    }

    public List<DiscordMember> GetMembers()
    {
        return DiscordContext.Members.ToList();
    }

    private void OnMessageCreated(object? sender, MessageCreateEvent e)
    {
        if (e.Author.IsBot) return;
        var channelId = e.Channel.Id.ToString(CultureInfo.InvariantCulture);
        if (!_monitoredChannelIds.Contains(channelId)) return;
        if (string.IsNullOrWhiteSpace(e.Content)) return;

        var entry = new MessageEntry
        {
            MessageId = e.Id.ToString(CultureInfo.InvariantCulture),
            Content = e.Content,
            AuthorName = e.Author.Username,
            AuthorId = e.Author.Id.ToString(CultureInfo.InvariantCulture),
            ChannelName = e.Channel.Name,
            ChannelId = channelId,
            GuildId = e.Guild?.Id.ToString(CultureInfo.InvariantCulture) ?? "",
            Timestamp = DateTimeOffset.UtcNow
        };

        _buffer.AddMessage(channelId, entry);
    }

    private void OnLog(object? sender, LogMessage msg)
    {
        if (msg.Level >= SimpleDiscordNet.Logging.LogLevel.Warning)
            ErrorOccurred?.Invoke(new Exception($"[{msg.Level}] {msg.Message}"));
    }
}
