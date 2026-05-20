using System.Collections.Concurrent;
using AI_Discord_Bot.Models;

namespace AI_Discord_Bot.Services;

public class MessageBufferService
{
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();
    private readonly ConcurrentDictionary<string, bool> _dirtyChannels = new();
    private int _windowSize;

    public MessageBufferService(int windowSize = 50)
    {
        _windowSize = windowSize;
    }

    public int WindowSize
    {
        get => _windowSize;
        set
        {
            _windowSize = value;
            foreach (var window in _windows.Values)
                window.Capacity = value;
        }
    }

    public void AddMessage(string channelId, MessageEntry entry)
    {
        var window = _windows.GetOrAdd(channelId, _ => new SlidingWindow(_windowSize));
        window.Add(entry);
        _dirtyChannels[channelId] = true;
    }

    public List<MessageEntry> GetWindow(string channelId)
    {
        if (!_windows.TryGetValue(channelId, out var window))
            return [];
        return window.GetSnapshot();
    }

    public void MarkClean(string channelId)
    {
        _dirtyChannels[channelId] = false;
    }

    public List<string> GetDirtyChannels()
    {
        return _dirtyChannels
            .Where(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public int TotalMessageCount => _windows.Sum(kvp => kvp.Value.Count);

    public int ActiveChannelCount => _dirtyChannels.Count(kvp => kvp.Value);

    private sealed class SlidingWindow
    {
        private readonly List<MessageEntry> _items = [];
        private readonly object _lock = new();

        public int Capacity { get; set; }

        public int Count
        {
            get { lock (_lock) return _items.Count; }
        }

        public SlidingWindow(int capacity)
        {
            Capacity = capacity;
        }

        public void Add(MessageEntry item)
        {
            lock (_lock)
            {
                _items.Add(item);
                while (_items.Count > Capacity)
                    _items.RemoveAt(0);
            }
        }

        public List<MessageEntry> GetSnapshot()
        {
            lock (_lock)
            {
                return [.._items];
            }
        }
    }
}
