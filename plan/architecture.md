# Architecture

## Overview

WPF desktop application (.NET 11.0-windows) that runs a Discord bot powered by an in-process LLM (LlamaSharp). The bot monitors assigned Discord channels, batches messages per channel into a concurrent buffer, and periodically invokes the LLM to analyze conversations and generate moderation reports.

## Libraries

| Library | Version | Purpose |
|---|---|---|
| SimpleDiscordDotNet | 1.10.9 | Discord Gateway/API client |
| LLamaSharp | 0.27.0 | In-process LLM inference (llama.cpp bindings) |
| LLamaSharp.Backend.Cuda12 | 0.27.0 | NVIDIA GPU support |
| LLamaSharp.Backend.Vulkan | 0.27.0 | AMD GPU support |
| LLamaSharp.Backend.Cpu | 0.27.0 | CPU fallback |

No other NuGet dependencies. Settings persisted as XML via `System.Xml.Serialization.XmlSerializer` (built-in BCL).

## Project Structure

```
AI Discord Bot/
├── Models/
│   ├── AppSettings.cs              -- XML-serializable settings
│   └── MessageEntry.cs             -- Single buffered message
├── Services/
│   ├── MessageBufferService.cs     -- ConcurrentDictionary<string, ConcurrentQueue<MessageEntry>>
│   ├── TimerService.cs             -- One-shot timer, restarted after processing cycle
│   ├── SettingsService.cs          -- XML load/save for AppSettings
│   ├── DiscordBotService.cs        -- Bot lifecycle, MessageCreated hook, channel listing, send embeds
│   ├── LlamaService.cs             -- Backend auto-detection, model load/unload, ChatSession, inference
│   ├── ModelDownloadService.cs     -- HTTP download GGUF files with progress
│   └── AnalysisService.cs          -- Cycle orchestrator: drain → format → LLM → parse → report
├── ViewModels/
│   ├── RelayCommand.cs             -- ICommand helper
│   └── MainViewModel.cs            -- All bindable state + commands
├── MainWindow.xaml                 -- Full UI
├── MainWindow.xaml.cs              -- Wire DataContext
├── App.xaml / App.xaml.cs          -- Application entry
└── Models/ (folder)                -- Downloaded GGUF files stored here
```

## Data Flow

```
Discord Gateway
      │  MessageCreated event
      ▼
DiscordBotService ──► MessageBufferService.AddMessage(channelId, entry)
                            │
                            │  (persistent ConcurrentDictionary<chId, ConcurrentQueue<MessageEntry>>)
                            │
                     ┌──────┘
                     │  Timer fires → stops itself
                     ▼
              AnalysisService.ProcessAllChannelsAsync()
                     │
                     │  For each channel with pending messages (sequential, await each):
                     │
                     ├─► buffer.DequeueAll(channelId)
                     ├─► Format messages into user prompt
                     ├─► llamaService.PromptAsync(systemPrompt, userPrompt)
                     ├─► Parse JSON response
                     ├─► If needsReport → discordService.SendEmbed(reportChannel, embed)
                     └─► Log to UI
                     │
                     ▼
              Restart timer (countdown to next cycle)
```

## Buffer Architecture

```csharp
ConcurrentDictionary<string, ConcurrentQueue<MessageEntry>>
       ▲                              ▲
       │ channelId "abc"              │ channelId "xyz"
       ▼                              ▼
  ┌──────────┐                  ┌──────────┐
  │ msg #1   │                  │ msg #1   │
  │ msg #2   │                  │ msg #2   │
  │ msg #3   │                  └──────────┘
  └──────────┘
```

- Queues are created once via `GetOrAdd` and persist for the app lifetime.
- `DequeueAll(channelId)` drains all messages from a queue but leaves the queue in the dictionary.
- No queue destruction/recreation — zero allocation overhead per cycle.
- New messages arriving during processing are queued concurrently and handled next cycle.

## Processing Cycle Guarantees

1. Timer fires → timer stops immediately (prevents overlap).
2. All channels with pending messages are processed sequentially (one at a time, `await` each).
3. After all channels processed → timer restarts.
4. If processing takes longer than the interval, no new cycle starts — no catastrophic overlap.
5. Exactly one LLM inference runs at any time.

## GPU Backend Auto-Detection

```
LoadModel:
  try Cuda12 backend → if success: "NVIDIA CUDA 12"
  catch → try Vulkan  → if success: "AMD Vulkan"
  catch → CPU         → "CPU (slow)"
```

No user selection required. Active backend displayed in UI for transparency.

## Model Management (Three Paths)

| Path | UI | Behavior |
|---|---|---|
| Curated list | ComboBox | Downloads known GGUF from HuggingFace URL |
| Custom URL | TextBox + Download button | Downloads from user-provided URL |
| Existing file | Browse button + path label | Uses already-downloaded GGUF file |

All downloaded models saved to `Models/` folder. Model path persisted to settings.xml.

## Settings

Persisted as XML via `XmlSerializer`. File: `settings.xml` alongside the executable.

```xml
<?xml version="1.0" encoding="utf-8"?>
<AppSettings>
  <BotToken></BotToken>
  <MonitoredChannelIds />
  <ReportChannelId></ReportChannelId>
  <ModelPath></ModelPath>
  <GpuLayerCount>25</GpuLayerCount>
  <AnalysisIntervalSeconds>30</AnalysisIntervalSeconds>
  <EnabledReportTypes>
    <string>ToxicChat</string>
    <string>Complaints</string>
    <string>ServerIssues</string>
    <string>ServerDown</string>
    <string>SimSpeedIssues</string>
    <string>PlayerComplaints</string>
  </EnabledReportTypes>
</AppSettings>
```

## LLM Prompt Design

System prompt constructed dynamically from selected report types:

```
You are a Discord server moderation assistant. Analyze the following chat messages
from ONE channel and determine if a report needs to be created.

Monitor for: Toxic Chat, Server Issues, Player Complaints [dynamic list]

Respond ONLY with a JSON object. No other text.

If issues found:
{"needsReport":true,"reportType":"...","severity":"low|medium|high|critical",
 "title":"...","summary":"...","details":"...","involvedUsers":[...]}

If no issues:
{"needsReport":false}

Rules: Only flag genuine issues. Casual conversation, minor disagreements,
and normal banter are NOT toxic. One person mentioning lag is not a "server down."
```

User prompt (per-channel batch):

```
Messages from the last 30 seconds:

[#general] Player1 (14:30): the server is lagging really bad today
[#general] Player2 (14:30): yeah i keep rubberbanding
[#chat] Player3 (14:31): admins please fix this garbage server
```

## UI Layout

```
┌──────────────────────────────────────────────────────────────────┐
│  AI Discord Bot — Server Moderation Assistant                    │
├────────────────────────────────┬─────────────────────────────────┤
│ ┌── DISCORD ────────────────┐ │ ┌── ACTIVITY LOG ─────────────┐ │
│ │ Token: [••••••••••]       │ │ │ [12:00:01] Bot connected    │ │
│ │ [Connect]         ● Online│ │ │ [12:00:02] LLM loaded       │ │
│ ├────────────────────────────┤ │ │ [12:00:30] Cycle start: 3  │ │
│ │ Channels to Monitor:       │ │ │   channels, 17 messages    │ │
│ │ ┌────────────────────┐     │ │ │ [12:00:32] #general: No   │ │
│ │ │ ☑ #general          │    │ │ │   issues                  │ │
│ │ │ ☑ #chat             │    │ │ │ [12:00:35] #support:      │ │
│ │ │ ☐ #off-topic        │    │ │ │   REPORT [HIGH] Toxic     │ │
│ │ └────────────────────┘     │ │ │ [12:00:37] #chat: No      │ │
│ ├────────────────────────────┤ │ │   issues                  │ │
│ │ Report Channel:            │ │ │ [12:00:37] Cycle complete │ │
│ │ [#admin-reports      ▼]   │ │ │   in 7.2s                 │ │
│ ├────────────────────────────┤ │ │                            │ │
│ │ ┌── LLM (LlamaSharp) ────┐ │ │ │                            │ │
│ │ │ Model: [Browse]        │ │ │ │                            │ │
│ │ │   llama3.2-3b.gguf     │ │ │ │                            │ │
│ │ │ Backend: CUDA 12       │ │ │ │                            │ │
│ │ │ GPU Layers: [25] ──○── │ │ │ │                            │ │
│ │ │ [Load]         ● Ready │ │ │ │                            │ │
│ │ │                        │ │ │ │                            │ │
│ │ │ Download Model:        │ │ │ │                            │ │
│ │ │ [Llama 3.2 3B      ▼] │ │ │ │                            │ │
│ │ │ or URL: [__________]   │ │ │ │                            │ │
│ │ │ [Download] ██████░░░░  │ │ │ │                            │ │
│ │ └────────────────────────┘ │ │ │                            │ │
│ ├────────────────────────────┤ │ │                            │ │
│ │ Report Options:            │ │ │                            │ │
│ │ ☑ Toxic Chat / Behavior   │ │ │                            │ │
│ │ ☑ Complaints               │ │ │                            │ │
│ │ ☑ Server Issues            │ │ │                            │ │
│ │ ☑ Server Down              │ │ │                            │ │
│ │ ☑ SimSpeed Issues          │ │ │                            │ │
│ │ ☑ Player Complaints        │ │ │                            │ │
│ ├────────────────────────────┤ │ │                            │ │
│ │ Interval: [30] seconds     │ │ │                            │ │
│ │ Buffer: 15 msgs / 3 ch     │ │ │                            │ │
│ │ [▶ Start] [■ Stop]         │ │ │                            │ │
│ └────────────────────────────┘ │ └────────────────────────────┘ │
├────────────────────────────────┴─────────────────────────────────┤
│  Status: Monitoring 3 channels — Next cycle in 22s               │
└──────────────────────────────────────────────────────────────────┘
```
