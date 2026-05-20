# Implementation Order

Files are built in dependency order. Each file must be complete before moving to the next.

## Phase 1: Foundation (no dependencies)

### 1.1 Models/AppSettings.cs
XML-serializable settings class. Properties for bot token, channel IDs, model path, GPU layers, interval, enabled report types.

### 1.2 Models/MessageEntry.cs
Simple data class: MessageId, Content, AuthorName, ChannelName, ChannelId, Timestamp.

### 1.3 ViewModels/RelayCommand.cs
Standard ICommand implementation with Action<object?> execute and Predicate<object?> canExecute.

## Phase 2: Settings Persistence

### 2.1 Services/SettingsService.cs
Load/save AppSettings via XmlSerializer. Default path: `settings.xml` in app directory. Creates default settings if file doesn't exist.

## Phase 3: Core Services (no Discord/LLM deps)

### 3.1 Services/MessageBufferService.cs
ConcurrentDictionary<string, ConcurrentQueue<MessageEntry>>. Methods: AddMessage, DequeueAll, GetActiveChannels, TotalMessageCount.

### 3.2 Services/TimerService.cs
One-shot timer. Start/Stop. Fires event on elapsed. Auto-stops after firing (caller must restart).

## Phase 4: Discord Integration

### 4.1 Services/DiscordBotService.cs
Builder pattern with token + intents (Guilds | GuildMessages | MessageContent). Exposes: ConnectAsync, DisconnectAsync, GetTextChannelsAsync, SendReportAsync. Hooks MessageCreated event → filters monitored channels → calls MessageBufferService.AddMessage. Reports status via events for UI.

## Phase 5: LLM Integration

### 5.1 Services/LlamaService.cs
Auto-detects GPU backend. LoadModelAsync(path, gpuLayers, progress). PromptAsync(systemPrompt, userPrompt) → returns full response string. UnloadModel(). Exposes IsLoaded, ActiveBackend, LoadedModelName.

### 5.2 Services/ModelDownloadService.cs
Downloads GGUF from URL to Models/ folder. Reports progress via IProgress<int>. Supports curated model list (name → URL mapping).

## Phase 6: Orchestration

### 6.1 Services/AnalysisService.cs
ProcessAllChannelsAsync(): drains each active channel → formats messages → calls LlamaService.PromptAsync → parses JSON → dispatches report embed via DiscordBotService → logs results. Called by timer.

## Phase 7: ViewModel

### 7.1 ViewModels/MainViewModel.cs
All bindable properties and commands. Manages lifetime of all services. Properties: BotToken, IsBotConnected, Channels (ObservableCollection), SelectedReportChannel, Ollama/LLM status, ModelPath, GpuLayerCount, SelectedReportTypes, IntervalSeconds, BufferStats, LogEntries (ObservableCollection<string>), StatusText. Commands: ConnectDiscord, DisconnectDiscord, RefreshChannels, LoadModel, UnloadModel, DownloadModel, BrowseModel, StartMonitoring, StopMonitoring.

## Phase 8: UI

### 8.1 MainWindow.xaml
Two-column Grid layout. Left: ScrollViewer with Discord config, LLM config, report options, interval, start/stop. Right: ListBox for log. Bottom: StatusBar. Use DataContext binding to MainViewModel.

### 8.2 MainWindow.xaml.cs
Set DataContext = new MainViewModel(). Wire Closing event for cleanup.

## Phase 9: Build Config

### 9.1 AI Discord Bot.csproj
Remove OllamaSharp package reference. Add LLamaSharp + three backend packages. Verify compilation.
