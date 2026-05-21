using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AI_Discord_Bot.Models;
using AI_Discord_Bot.Services;

namespace AI_Discord_Bot.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;
    private readonly MessageBufferService _buffer;
    private readonly TimerService _timer;
    private readonly DiscordBotService _discord;
    private readonly LlamaService _llama;
    private readonly ModelDownloadService _downloader;
    private readonly AnalysisService _analysis;
    private AppSettings _appSettings = new();
    private CancellationTokenSource? _saveCts;
    private readonly DispatcherTimer _statusTimer;
    private DateTimeOffset _nextCycleAt;
    private bool _isProcessing;
    private ConsoleTextWriter? _consoleWriter;

    public MainViewModel()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var settingsPath = Path.Combine(exeDir, "settings.xml");
        _settings = new SettingsService(settingsPath);
        _appSettings = _settings.Load();

        _buffer = new MessageBufferService(_appSettings.WindowSize);
        _timer = new TimerService(_appSettings.AnalysisIntervalSeconds);
        _timer.Elapsed += OnTimerElapsed;

        var monitoredIds = new HashSet<string>(_appSettings.MonitoredChannelIds);
        _discord = new DiscordBotService(_buffer, monitoredIds);
        _discord.StatusChanged += () =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsBotConnected));
                OnPropertyChanged(nameof(BotStatus));
                OnPropertyChanged(nameof(CanConnectDiscord));
                OnPropertyChanged(nameof(CanStart));
                RefreshCommands();
            });
        };

        _llama = new LlamaService { Temperature = _appSettings.Temperature };
        _downloader = new ModelDownloadService();

        _analysis = new AnalysisService(_buffer, _llama, _discord, _appSettings.EnabledReportTypes, _appSettings.CustomRules);
        _analysis.LogMessage += (msg, level) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AddLogEntry(msg, level);
                UpdateBufferStats();
            });
        };

        UpdateBufferStats();

        _statusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal,
            (_, _) => OnPropertyChanged(nameof(StatusText)), Application.Current.Dispatcher);
        _statusTimer.Stop();

        _consoleWriter = new ConsoleTextWriter(line => Application.Current.Dispatcher.Invoke(() => AppendConsole(line)));
        Console.SetOut(_consoleWriter);
        Console.SetError(_consoleWriter);

        LlamaService.EnableNativeLogging(line => Application.Current.Dispatcher.Invoke(() => AppendConsole(line)));
    }

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_appSettings.BotToken))
        {
            await ConnectDiscordAsync();
            if (_discord.IsConnected)
            {
                LogDebug("Waiting for guild data...");
                for (var i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    if (_discord.GetGuilds().Count > 0) break;
                }
                await RefreshChannelsAsync();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // --- Discord ---

    public string BotToken
    {
        get => _appSettings.BotToken;
        set { _appSettings.BotToken = value; OnPropertyChanged(); ScheduleSave(); }
    }

    public bool IsBotConnected => _discord.IsConnected;

    public string BotStatus => _discord.IsConnected ? "● Online" : "○ Offline";

    public bool CanConnectDiscord => !_discord.IsConnected && !string.IsNullOrWhiteSpace(BotToken);

    // --- Channels ---

    public ObservableCollection<ChannelItem> Channels { get; } = [];

    private ChannelItem? _selectedReportChannel;
    public ChannelItem? SelectedReportChannel
    {
        get => _selectedReportChannel;
        set
        {
            _selectedReportChannel = value;
            _appSettings.ReportChannelId = value?.ChannelId ?? "";
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public ObservableCollection<UserItem> Members { get; } = [];

    private UserItem? _selectedReportDmUser;
    public UserItem? SelectedReportDmUser
    {
        get => _selectedReportDmUser;
        set
        {
            _selectedReportDmUser = value;
            _appSettings.ReportDmUserId = value?.UserId ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReportDmUserText));
            ScheduleSave();
        }
    }

    public string ReportDmUserText
    {
        get => _selectedReportDmUser?.DisplayText ?? _appSettings.ReportDmUserId;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _appSettings.ReportDmUserId = "";
                _selectedReportDmUser = null;
                OnPropertyChanged(nameof(SelectedReportDmUser));
                OnPropertyChanged();
                ScheduleSave();
                return;
            }

            var match = Members.FirstOrDefault(m =>
                m.DisplayText.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                m.UserId.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                SelectedReportDmUser = match;
            }
            else if (ulong.TryParse(value, out _))
            {
                _appSettings.ReportDmUserId = value;
                _selectedReportDmUser = null;
                OnPropertyChanged(nameof(SelectedReportDmUser));
                OnPropertyChanged();
                ScheduleSave();
            }
        }
    }

    // --- LLM ---

    public string ModelPath
    {
        get => _appSettings.ModelPath;
        set { _appSettings.ModelPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanLoadModel)); ScheduleSave(); }
    }

    public bool IsModelLoaded => _llama.IsLoaded;

    public string ActiveBackend => _llama.IsLoaded ? _llama.ActiveBackend : "—";

    public bool CanLoadModel => !_llama.IsLoaded && !string.IsNullOrWhiteSpace(ModelPath) && File.Exists(ModelPath);

    public int GpuLayerCount
    {
        get => _appSettings.GpuLayerCount;
        set { _appSettings.GpuLayerCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDetectContext)); ScheduleSave(); }
    }

    public List<string> BackendModeOptions { get; } = ["Automatic", "GPU", "CPU"];

    private int _savedGpuLayerCount = 25;

    public string BackendMode
    {
        get => _appSettings.BackendMode;
        set
        {
            _appSettings.BackendMode = value;
            if (value == "CPU")
            {
                _savedGpuLayerCount = GpuLayerCount;
                GpuLayerCount = 0;
            }
            else if (value is "GPU" or "Automatic" && GpuLayerCount == 0)
            {
                GpuLayerCount = _savedGpuLayerCount;
            }
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public bool FlashAttention
    {
        get => _appSettings.FlashAttention;
        set { _appSettings.FlashAttention = value; OnPropertyChanged(); ScheduleSave(); }
    }

    public float Temperature
    {
        get => _appSettings.Temperature;
        set
        {
            _appSettings.Temperature = value;
            _llama.Temperature = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TemperatureText));
            ScheduleSave();
        }
    }

    public string TemperatureText => Temperature.ToString("F1");

    public bool AutoContextSize
    {
        get => _appSettings.AutoContextSize;
        set
        {
            _appSettings.AutoContextSize = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManualContext));
            OnPropertyChanged(nameof(CanDetectContext));
            if (value)
                ContextSize = LlamaService.DetectOptimalContextSize(GpuLayerCount);
            ScheduleSave();
        }
    }

    public bool IsManualContext => !AutoContextSize;

    public bool CanDetectContext => !AutoContextSize || true;

    public int ContextSize
    {
        get => _appSettings.ContextSize;
        set
        {
            _appSettings.ContextSize = Math.Clamp(value, 1024, 262144);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContextSizeText));
            ScheduleSave();
        }
    }

    public string ContextSizeText
    {
        get => ContextSize.ToString();
        set
        {
            if (int.TryParse(value, out var parsed))
                ContextSize = parsed;
        }
    }

    public List<string> ContextSizeOptions { get; } = ["2048", "4096", "8192", "16384", "32768", "65536", "131072", "262144"];

    public bool CanUnloadModel => _llama.IsLoaded;

    // --- Download ---

    public bool IsDownloading => _downloader.IsDownloading;

    private int _downloadProgress;
    public int DownloadProgress
    {
        get => _downloadProgress;
        set { _downloadProgress = value; OnPropertyChanged(); }
    }

    public List<string> CuratedModelNames { get; } = [..ModelDownloadService.CuratedModels.Keys];

    private string _selectedCuratedModel = "";
    public string SelectedCuratedModel
    {
        get => _selectedCuratedModel;
        set { _selectedCuratedModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownload)); }
    }

    private string _customDownloadUrl = "";
    public string CustomDownloadUrl
    {
        get => _customDownloadUrl;
        set { _customDownloadUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanDownload)); }
    }

    public bool CanDownload => !_downloader.IsDownloading &&
        (!string.IsNullOrWhiteSpace(_selectedCuratedModel) || !string.IsNullOrWhiteSpace(_customDownloadUrl));

    // --- Report Options ---

    public bool ReportToxicChat
    {
        get => _appSettings.EnabledReportTypes.Contains("ToxicChat");
        set { ToggleReportType("ToxicChat", value); }
    }

    public bool ReportComplaints
    {
        get => _appSettings.EnabledReportTypes.Contains("Complaints");
        set { ToggleReportType("Complaints", value); }
    }

    public bool ReportServerIssues
    {
        get => _appSettings.EnabledReportTypes.Contains("ServerIssues");
        set { ToggleReportType("ServerIssues", value); }
    }

    public bool ReportServerDown
    {
        get => _appSettings.EnabledReportTypes.Contains("ServerDown");
        set { ToggleReportType("ServerDown", value); }
    }

    public bool ReportSimSpeedIssues
    {
        get => _appSettings.EnabledReportTypes.Contains("SimSpeedIssues");
        set { ToggleReportType("SimSpeedIssues", value); }
    }

    public bool ReportPlayerComplaints
    {
        get => _appSettings.EnabledReportTypes.Contains("PlayerComplaints");
        set { ToggleReportType("PlayerComplaints", value); }
    }

    // --- Custom Rules ---

    public string CustomRules
    {
        get => _appSettings.CustomRules;
        set
        {
            _appSettings.CustomRules = value;
            _analysis.CustomRules = value;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public static string DefaultRules { get; } = Models.DefaultBotRules.Text;

    private RelayCommand? _resetRulesCommand;
    public RelayCommand ResetRulesCommand => _resetRulesCommand ??= new RelayCommand(_ => ResetRules());

    private void ResetRules()
    {
        var result = MessageBox.Show(
            "This will erase your current rules and restore the defaults.\n\nContinue?",
            "Reset Rules to Default",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            CustomRules = DefaultRules;
        }
    }

    // --- Controls ---

    public int IntervalSeconds
    {
        get => _appSettings.AnalysisIntervalSeconds;
        set
        {
            _appSettings.AnalysisIntervalSeconds = Math.Max(5, value);
            _timer.IntervalSeconds = _appSettings.AnalysisIntervalSeconds;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    public int WindowSize
    {
        get => _appSettings.WindowSize;
        set
        {
            _appSettings.WindowSize = Math.Max(5, value);
            _buffer.WindowSize = _appSettings.WindowSize;
            OnPropertyChanged();
            ScheduleSave();
        }
    }

    private string _bufferStats = "0 msgs / 0 channels";
    public string BufferStats
    {
        get => _bufferStats;
        set { _bufferStats = value; OnPropertyChanged(); }
    }

    public bool IsMonitoring => _isMonitoring;
    private bool _isMonitoring;

    public bool CanStart => _discord.IsConnected && _llama.IsLoaded && !_timer.IsRunning;

    public bool CanStop => _timer.IsRunning;

    public string StatusText
    {
        get
        {
            if (!IsMonitoring) return "Idle";
            if (_isProcessing) return "Processing messages...";
            var remaining = _nextCycleAt - DateTimeOffset.Now;
            if (remaining.TotalSeconds <= 0) return "Next cycle starting...";
            return remaining switch
            {
                { TotalMinutes: >= 1 } m => $"Next cycle in {m.Minutes}m {m.Seconds}s",
                _ => $"Next cycle in {remaining.Seconds}s"
            };
        }
    }

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    private string _consoleOutput = "";
    public string ConsoleOutput
    {
        get => _consoleOutput;
        set { _consoleOutput = value; OnPropertyChanged(); }
    }

    public void AppendConsole(string line)
    {
        _consoleOutput += line + Environment.NewLine;
        OnPropertyChanged(nameof(ConsoleOutput));
    }

    // --- Commands ---

    private RelayCommand? _connectDiscordCommand;
    public RelayCommand ConnectDiscordCommand => _connectDiscordCommand ??= new RelayCommand(async _ => await ConnectDiscordAsync(), _ => CanConnectDiscord);

    private RelayCommand? _disconnectDiscordCommand;
    public RelayCommand DisconnectDiscordCommand => _disconnectDiscordCommand ??= new RelayCommand(async _ => await DisconnectDiscordAsync(), _ => IsBotConnected);

    private RelayCommand? _refreshChannelsCommand;
    public RelayCommand RefreshChannelsCommand => _refreshChannelsCommand ??= new RelayCommand(async _ => await RefreshChannelsAsync(), _ => IsBotConnected);

    private RelayCommand? _browseModelCommand;
    public RelayCommand BrowseModelCommand => _browseModelCommand ??= new RelayCommand(_ => BrowseModel());

    private RelayCommand? _loadModelCommand;
    public RelayCommand LoadModelCommand => _loadModelCommand ??= new RelayCommand(async _ => await LoadModelAsync(), _ => CanLoadModel);

    private RelayCommand? _unloadModelCommand;
    public RelayCommand UnloadModelCommand => _unloadModelCommand ??= new RelayCommand(_ => UnloadModel(), _ => CanUnloadModel);

    private RelayCommand? _downloadModelCommand;
    public RelayCommand DownloadModelCommand => _downloadModelCommand ??= new RelayCommand(async _ => await DownloadModelAsync(), _ => CanDownload);

    private RelayCommand? _cancelDownloadCommand;
    public RelayCommand CancelDownloadCommand => _cancelDownloadCommand ??= new RelayCommand(_ => CancelDownload(), _ => IsDownloading);

    private RelayCommand? _startMonitoringCommand;
    public RelayCommand StartMonitoringCommand => _startMonitoringCommand ??= new RelayCommand(_ => StartMonitoring(), _ => CanStart);

    private RelayCommand? _stopMonitoringCommand;
    public RelayCommand StopMonitoringCommand => _stopMonitoringCommand ??= new RelayCommand(_ => StopMonitoring(), _ => CanStop);

    // --- Methods ---

    private void RefreshCommands()
    {
        Application.Current.Dispatcher.Invoke(CommandManager.InvalidateRequerySuggested);
    }

    private async Task ConnectDiscordAsync()
    {
        try
        {
            LogInfo("Connecting to Discord...");
            await _discord.ConnectAsync(BotToken);
            LogInfo("Bot connected");
            OnPropertyChanged(nameof(CanStart));
RefreshCommands();
        }
        catch (Exception ex)
        {
            LogError($"Discord connection failed: {ex.Message}");
        }
    }

    private async Task DisconnectDiscordAsync()
    {
        if (IsMonitoring) StopMonitoring();
        await _discord.DisconnectAsync();
        Channels.Clear();
        LogInfo("Bot disconnected");
        OnPropertyChanged(nameof(CanStart));
RefreshCommands();
    }

    private async Task RefreshChannelsAsync()
    {
        try
        {
            Channels.Clear();
            Channels.Add(new ChannelItem { ChannelId = "", ChannelName = "(none)", GuildName = "" });
            var guilds = _discord.GetGuilds();

            foreach (var guild in guilds)
            {
                var channels = await _discord.GetTextChannelsAsync(guild.Id.ToString());
                foreach (var channel in channels.OrderBy(c => c.Name))
                {
                    Channels.Add(new ChannelItem
                    {
                        ChannelId = channel.Id.ToString(),
                        ChannelName = $"#{channel.Name}",
                        GuildName = guild.Name,
                        IsSelected = _appSettings.MonitoredChannelIds.Contains(channel.Id.ToString())
                    });
                }
            }

            if (Channels.Count == 0)
                LogWarning("No text channels found. Ensure the bot has the required permissions.");
            else
                LogInfo($"Found {Channels.Count} text channel(s) across {guilds.Count} guild(s)");

            RestoreReportChannelSelection();
            await RefreshMembersAsync();
        }
        catch (Exception ex)
        {
            LogError($"Failed to refresh channels: {ex.Message}");
        }
    }

    private void RestoreReportChannelSelection()
    {
        if (string.IsNullOrWhiteSpace(_appSettings.ReportChannelId)) return;
        var match = Channels.FirstOrDefault(c => c.ChannelId == _appSettings.ReportChannelId);
        if (match is not null)
            SelectedReportChannel = match;
    }

    private async Task RefreshMembersAsync()
    {
        try
        {
            Members.Clear();
            Members.Add(new UserItem { UserId = "", UserName = "(none)", GuildName = "" });
            var members = _discord.GetMembers();

            foreach (var member in members.OrderBy(m => m.User.Username))
            {
                Members.Add(new UserItem
                {
                    UserId = member.User.Id.ToString(),
                    UserName = member.User.Username,
                    GuildName = member.Guild?.Name ?? ""
                });
            }

            RestoreReportDmUserSelection();
        }
        catch (Exception ex)
        {
            LogError($"Failed to refresh members: {ex.Message}");
        }
    }

    private void RestoreReportDmUserSelection()
    {
        if (string.IsNullOrWhiteSpace(_appSettings.ReportDmUserId)) return;
        var match = Members.FirstOrDefault(m => m.UserId == _appSettings.ReportDmUserId);
        if (match is not null)
            SelectedReportDmUser = match;
    }

    private void BrowseModel()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "GGUF Files (*.gguf)|*.gguf|All Files (*.*)|*.*",
            Title = "Select GGUF Model File"
        };

        if (dialog.ShowDialog() == true)
        {
            ModelPath = dialog.FileName;
        }
    }

    private async Task LoadModelAsync()
    {
        try
        {
            LogInfo($"Loading model: {ModelPath}");
            var progress = new Progress<string>(msg => LogInfo(msg));
            await _llama.LoadModelAsync(ModelPath, GpuLayerCount, ContextSize, FlashAttention, progress);
            LogInfo($"Model loaded. Backend: {_llama.ActiveBackend}");
            OnPropertyChanged(nameof(IsModelLoaded));
            OnPropertyChanged(nameof(ActiveBackend));
            OnPropertyChanged(nameof(CanLoadModel));
            OnPropertyChanged(nameof(CanUnloadModel));
            OnPropertyChanged(nameof(CanStart));
RefreshCommands();
        }
        catch (Exception ex)
        {
            LogError($"Failed to load model: {ex.Message}");
        }
    }

    private void UnloadModel()
    {
        _llama.UnloadModel();
        LogInfo("Model unloaded");
        OnPropertyChanged(nameof(IsModelLoaded));
        OnPropertyChanged(nameof(ActiveBackend));
        OnPropertyChanged(nameof(CanLoadModel));
        OnPropertyChanged(nameof(CanUnloadModel));
        OnPropertyChanged(nameof(CanStart));
RefreshCommands();
    }

    private async Task DownloadModelAsync()
    {
        try
        {
            var url = !string.IsNullOrWhiteSpace(_customDownloadUrl)
                ? _customDownloadUrl
                : ModelDownloadService.CuratedModels.GetValueOrDefault(_selectedCuratedModel, "");

            if (string.IsNullOrWhiteSpace(url))
            {
                LogWarning("No download URL specified.");
                return;
            }

            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var modelsFolder = Path.Combine(exeDir, "Models");
            LogInfo($"Downloading model from {url}...");

            OnPropertyChanged(nameof(IsDownloading));
            OnPropertyChanged(nameof(CanDownload));
            DownloadProgress = 0;

            var progress = new Progress<int>(p => DownloadProgress = p);
            var savePath = await _downloader.DownloadAsync(url, modelsFolder, progress);

            ModelPath = savePath;
            LogInfo($"Download complete: {Path.GetFileName(savePath)}");

            OnPropertyChanged(nameof(IsDownloading));
            OnPropertyChanged(nameof(CanDownload));
            DownloadProgress = 0;
        }
        catch (OperationCanceledException)
        {
            LogInfo("Download cancelled");
            DownloadProgress = 0;
            OnPropertyChanged(nameof(IsDownloading));
        }
        catch (Exception ex)
        {
            LogError($"Download failed: {ex.Message}");
            DownloadProgress = 0;
            OnPropertyChanged(nameof(IsDownloading));
        }
    }

    private void CancelDownload()
    {
        _downloader.CancelDownload();
    }

    private void StartMonitoring()
    {
        var ids = Channels.Where(c => c.IsSelected).Select(c => c.ChannelId).ToList();
        _appSettings.MonitoredChannelIds = ids;
        _discord.UpdateMonitoredChannels([..ids]);
        SaveSettingsNow();

        _isMonitoring = true;
        _nextCycleAt = DateTimeOffset.Now + TimeSpan.FromSeconds(IntervalSeconds);
        _timer.Start();
        _statusTimer.Start();
        LogInfo($"Monitoring started: {ids.Count} channel(s), every {IntervalSeconds}s");
        OnPropertyChanged(nameof(IsMonitoring));
        OnPropertyChanged(nameof(CanStart));
        RefreshCommands();
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(StatusText));
    }

    private void StopMonitoring()
    {
        _isMonitoring = false;
        _timer.Stop();
        _statusTimer.Stop();
        _isProcessing = false;
        LogInfo("Monitoring stopped");
        OnPropertyChanged(nameof(IsMonitoring));
        OnPropertyChanged(nameof(CanStart));
        RefreshCommands();
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(StatusText));
    }

    private async Task OnTimerElapsed()
    {
        _isProcessing = true;
        Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(StatusText)));

        try
        {
            await _analysis.ProcessAllChannelsAsync(_appSettings.ReportChannelId, _appSettings.ReportDmUserId);
        }
        catch (Exception ex)
        {
            LogError($"Analysis error: {ex.Message}");
        }

        _isProcessing = false;
        _nextCycleAt = DateTimeOffset.Now + TimeSpan.FromSeconds(IntervalSeconds);
        Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(StatusText)));
    }

    private void ToggleReportType(string type, bool enabled)
    {
        if (enabled && !_appSettings.EnabledReportTypes.Contains(type))
            _appSettings.EnabledReportTypes.Add(type);
        else if (!enabled)
            _appSettings.EnabledReportTypes.Remove(type);
        ScheduleSave();
    }

    private void UpdateBufferStats()
    {
        BufferStats = $"{_buffer.TotalMessageCount} msgs / {_buffer.ActiveChannelCount} channels";
    }

    private void LogInfo(string message) => Log(LogLevel.Info, message);
    private void LogWarning(string message) => Log(LogLevel.Warning, message);
    private void LogError(string message) => Log(LogLevel.Error, message);
    private void LogDebug(string message) => Log(LogLevel.Debug, message);

    private void Log(LogLevel level, string message)
    {
        Application.Current.Dispatcher.Invoke(() => AddLogEntry(message, level));
    }

    private void AddLogEntry(string message, LogLevel level)
    {
        LogEntries.Add(new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Message = message
        });
        while (LogEntries.Count > 100)
            LogEntries.RemoveAt(0);
    }

    public bool AutoScrollLog { get; set; } = true;

    private void ScheduleSave()
    {
        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;
        _ = Task.Delay(2000, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                SaveSettingsNow();
        }, TaskScheduler.Default);
    }

    private void SaveSettingsNow()
    {
        try { _settings.Save(_appSettings); }
        catch { /* non-fatal */ }
    }

    public void SaveNow()
    {
        _saveCts?.Cancel();
        SaveSettingsNow();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ChannelItem : INotifyPropertyChanged
{
    public string ChannelId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string GuildName { get; set; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public string DisplayText => $"{ChannelName} ({GuildName})";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class UserItem : INotifyPropertyChanged
{
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string GuildName { get; set; } = "";

    public string DisplayText => string.IsNullOrWhiteSpace(GuildName)
        ? $"@{UserName}"
        : $"@{UserName} ({GuildName})";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
