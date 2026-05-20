using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
            if (_viewModel.AutoScrollLog && LogListBox.Items.Count > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (LogListBox.Items.Count > 0)
                        LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                }), DispatcherPriority.Background);
            }
        };
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.BotToken = pb.Password;
    }

    private void LogList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            var element = (e.OriginalSource as FrameworkElement)?.DataContext;
            if (element is not null)
                listBox.SelectedItem = element;
        }
    }

    private void LogContext_Copy(object sender, RoutedEventArgs e)
    {
        if (LogListBox.SelectedItem is string text)
            Clipboard.SetText(text);
    }
}

