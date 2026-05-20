namespace AI_Discord_Bot.Services;

public class TimerService
{
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private int _intervalSeconds;

    public event Func<Task>? Elapsed;

    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set
        {
            _intervalSeconds = value;
            if (_timer is not null)
            {
                Stop();
                Start();
            }
        }
    }

    public bool IsRunning => _timer is not null;

    public TimerService(int intervalSeconds)
    {
        _intervalSeconds = intervalSeconds;
    }

    public void Start()
    {
        if (_timer is not null)
            return;

        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
        _ = RunLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _timer?.Dispose();
        _timer = null;
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool ticked;
            try
            {
                ticked = await _timer!.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!ticked)
                break;

            var old = Interlocked.Exchange(ref _timer, null);
            old?.Dispose();

            try
            {
                if (Elapsed is not null)
                    await Elapsed.Invoke();
            }
            catch
            {
                // event handler errors are non-fatal to the timer loop
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    _timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
            }
        }
    }
}
