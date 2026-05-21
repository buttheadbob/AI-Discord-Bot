using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using AI_Discord_Bot.Models;
using AI_Discord_Bot.ViewModels;

namespace AI_Discord_Bot;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        TokenBox.Password = _viewModel.BotToken;

        Closing += (_, _) =>
        {
            _viewModel.StopMonitoringCommand.Execute(null);
            _viewModel.SaveNow();
        };

        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
        };

        _viewModel.LogEntries.CollectionChanged += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(UpdateLogTextBox), DispatcherPriority.Background);
        };

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ConsoleOutput))
            {
                Dispatcher.BeginInvoke(new Action(() => ConsoleTextBox.ScrollToEnd()), DispatcherPriority.Background);
            }
        };
    }

    private void UpdateLogTextBox()
    {
        var doc = LogRichTextBox.Document;
        var entries = _viewModel.LogEntries;

        if (entries.Count == 0)
        {
            doc.Blocks.Clear();
            return;
        }

        var existingCount = doc.Blocks.Count;

        for (var i = existingCount; i < entries.Count; i++)
        {
            AppendLogEntry(entries[i]);
        }

        while (doc.Blocks.Count > 100)
            doc.Blocks.Remove(doc.Blocks.FirstBlock);

        if (_viewModel.AutoScrollLog && doc.Blocks.Count > 0)
            LogRichTextBox.ScrollToEnd();
    }

    private void AppendLogEntry(LogEntry entry)
    {
        var para = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };

        para.Inlines.Add(new Run(entry.Timestamp.ToString("HH:mm:ss.ffff") + " ") { Foreground = Brushes.DarkGray });

        var levelBrush = entry.Level switch
        {
            LogLevel.Debug => Brushes.Gray,
            LogLevel.Info => Brushes.DodgerBlue,
            LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xD7, 0x8A, 0x00)),
            LogLevel.Error => Brushes.Red,
            _ => Brushes.Black
        };

        var levelText = entry.Level switch
        {
            LogLevel.Debug => "Debug",
            LogLevel.Info => "Info",
            LogLevel.Warning => "Warn",
            LogLevel.Error => "Error",
            _ => "Info"
        };

        para.Inlines.Add(new Run($"[{levelText.PadRight(5)}] ") { Foreground = levelBrush });
        para.Inlines.Add(new Run(entry.Message) { Foreground = Brushes.Black });

        LogRichTextBox.Document.Blocks.Add(para);
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.BotToken = pb.Password;
    }
}
