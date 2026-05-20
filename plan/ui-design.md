# UI Design

## Window

| Property | Value |
|---|---|
| Title | AI Discord Bot — Server Moderation Assistant |
| Width | 960 |
| Height | 640 |
| MinWidth | 800 |
| MinHeight | 500 |
| ResizeMode | CanResize |

## Layout

Two-column Grid with a StatusBar at the bottom:

```
┌──────────────────────────────────────────────────────────────────┐
│  Title Bar                                                       │
├────────────────────────────────┬─────────────────────────────────┤
│  LEFT PANEL (380px)            │  RIGHT PANEL (580px)            │
│  ScrollViewer                  │  GroupBox: "Activity Log"       │
│                                │  ListBox (auto-scroll)          │
│  ┌── DISCORD ────────────┐    │                                 │
│  │                        │    │                                 │
│  ├── MONITORED CHANNELS ─┤    │                                 │
│  │                        │    │                                 │
│  ├── REPORTING ──────────┤    │                                 │
│  │                        │    │                                 │
│  ├── LLM ────────────────┤    │                                 │
│  │                        │    │                                 │
│  ├── REPORT OPTIONS ─────┤    │                                 │
│  │                        │    │                                 │
│  ├── CONTROLS ───────────┤    │                                 │
│  │                        │    │                                 │
│  └────────────────────────┘    │                                 │
├────────────────────────────────┴─────────────────────────────────┤
│  StatusBar: Status text                               Buffer stats│
└──────────────────────────────────────────────────────────────────┘
```

## Left Panel Sections

### Section 1: Discord Connection
```
Label: "Discord Connection"
├── TextBox: Bot Token (PasswordBox, width matches)
├── Button: "Connect" / "Disconnect"
└── TextBlock: Status indicator (● Online / ○ Offline)
```

### Section 2: Monitored Channels
```
Label: "Channels to Monitor"
├── Button: "Refresh" (top-right)
└── ListBox with CheckBox items
    └── ☑ #general
    └── ☑ #chat
    └── ☐ #off-topic
```
Items are DataTemplate: CheckBox with channel name. Two-way binding to MonitoredChannel.IsSelected.

### Section 3: Report Channel
```
Label: "Report Channel"
└── ComboBox: All text channels from connected guilds
    └── SelectedItem bound to ReportChannelId
```

### Section 4: LLM (LlamaSharp)
```
Label: "LLM (LlamaSharp)"
├── Label: "Model File"
├── Grid: TextBox (read-only path) + Button "Browse..."
├── TextBlock: "Backend: CUDA 12" (auto-detected)
├── StackPanel (horizontal):
│   ├── Label: "GPU Layers:"
│   └── Slider: 0–99, bound to GpuLayerCount
├── Button: "Load Model" / "Unload Model"
├── TextBlock: Status (● Ready / ○ No model / Loading...)
├── Separator
├── Label: "Download Model"
├── ComboBox: Curated model list (Llama 3.2 3B, Phi-4-mini, Qwen 3 4B, Gemma 3 4B, etc.)
├── TextBox: "Or enter download URL..." (optional, overrides dropdown)
├── Button: "Download" (with IsDefault when URL focused)
└── ProgressBar: Download progress (0–100, visible during download)
```

### Section 5: Report Options
```
Label: "Report Types"
├── CheckBox: "Toxic Chat / Behavior"
├── CheckBox: "Complaints"
├── CheckBox: "Server Issues"
├── CheckBox: "Server Down"
├── CheckBox: "SimSpeed Issues"
└── CheckBox: "Player Complaints"
```
Each CheckBox two-way bound to a bool in the ViewModel.

### Section 6: Controls
```
Label: "Monitoring"
├── StackPanel (horizontal):
│   ├── Label: "Interval:"
│   ├── TextBox: numeric, Width=50, bound to IntervalSeconds
│   └── Label: "seconds"
├── TextBlock: "Buffer: 15 msgs across 3 channels"
├── StackPanel (horizontal):
│   ├── Button: "▶ Start" (enabled when bot+llm both ready)
│   └── Button: "■ Stop" (enabled when monitoring)
└── TextBlock: "Next cycle in 22s"
```

## Right Panel: Activity Log

```
GroupBox Header: "Activity Log"
└── ListBox
    └── ItemsSource: ObservableCollection<LogEntry>
    └── ItemTemplate: TextBlock showing timestamp + message
    └── Auto-scroll to bottom on new entry
```

Log entries example:
```
[14:00:01] Bot connected as "ModBot#1234"
[14:00:02] LLM loaded: llama3.2-3b.gguf (CUDA 12)
[14:00:30] Cycle start: 3 channels, 17 messages
[14:00:32] #general: No issues found
[14:00:35] #support: REPORT SENT [HIGH] Toxic behavior detected
[14:00:37] #chat: No issues found
[14:00:37] Cycle complete in 7.2s — next cycle in 22s
[14:01:00] Cycle start: 1 channel, 5 messages
[14:01:01] #general: No issues found
[14:01:01] Cycle complete in 0.9s — next cycle in 29s
```

## StatusBar

```
Left: Current status text (e.g. "Monitoring 3 channels — Next cycle in 22s")
Right: Buffer stats (e.g. "15 msgs / 3 channels")
```

## Colors & Styling

- Default WPF theme (no custom styling libraries).
- Status indicators: Green ellipse for connected/ready, Red for disconnected, Yellow for processing.
- Log entries: Default + Red for report-sent entries.

## Keyboard Shortcuts

| Key | Action |
|---|---|
| F5 | Refresh channel list |
| Ctrl+Enter | Connect Discord (when token filled) |

## Disable Logic

Controls enabled/disabled based on state:
- "Connect Discord" enabled only when token is filled and not connected.
- "Refresh Channels" enabled only when bot is connected.
- "Load Model" enabled only when path is set and not loaded.
- "Download" enabled only when URL or curated model selected.
- "Start" enabled only when both Discord and LLM are connected/loaded.
- "Stop" enabled only when monitoring is active.
