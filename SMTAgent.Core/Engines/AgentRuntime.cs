using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class AgentRuntime
{
    private readonly YahooFinanceMarketDataProvider _yahooFinanceMarketDataProvider = new();
    private readonly MockMarketDataProvider _mockMarketDataProvider = new();
    private readonly SwingDetector _swingDetector = new();
    private readonly SmtDetector _smtDetector = new();
    private readonly HashSet<string> _emittedSignalKeys = [];
    private readonly Dictionary<string, DateTime> _lastSignalByPair = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _runTask;

    public event Action<MarketDataSnapshot>? MarketDataUpdated;
    public event Action<SmtSignal>? SignalDetected;
    public event Action<string>? LogGenerated;
    public event Action<AgentStatus>? StatusChanged;

    public AgentStatus Status { get; private set; } = AgentStatus.Stopped;
    public AgentSettings Settings { get; } = new();
    public List<Candle> EsCandles { get; } = [];
    public List<Candle> NqCandles { get; } = [];
    public List<SwingPoint> EsSwings { get; } = [];
    public List<SwingPoint> NqSwings { get; } = [];

    public void Start()
    {
        if (Status == AgentStatus.Paused)
        {
            SetStatus(AgentStatus.Running);
            return;
        }

        Stop();
        ResetState();

        Settings.Timeframe = "15m";
        _cancellationTokenSource = new CancellationTokenSource();
        _runTask = RunAsync(_cancellationTokenSource.Token);
    }

    public void Pause()
    {
        if (Status == AgentStatus.Running)
        {
            SetStatus(AgentStatus.Paused);
        }
    }

    public void Stop()
    {
        if (Status == AgentStatus.Stopped)
        {
            return;
        }

        _cancellationTokenSource?.Cancel();
        SetStatus(AgentStatus.Stopped);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        SetStatus(AgentStatus.Running);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (Status == AgentStatus.Paused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(250, cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await RefreshAsync(cancellationToken);
                await Task.Delay(Settings.RefreshRate, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            // Stop intentionally cancels the polling loop.
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        MarketDataSet data;
        var provider = Settings.DataProvider;
        var status = DataConnectionStatus.Connected;
        string? statusMessage = null;

        try
        {
            data = provider == DataProviderMode.YahooFinance
                ? await _yahooFinanceMarketDataProvider.FetchAsync(cancellationToken)
                : _mockMarketDataProvider.Generate("15m");
        }
        catch (Exception exception) when (provider == DataProviderMode.YahooFinance)
        {
            data = _mockMarketDataProvider.Generate("15m");
            provider = DataProviderMode.Mock;
            status = DataConnectionStatus.Error;
            statusMessage = $"Yahoo Finance unavailable. Showing mock fallback: {exception.Message}";
        }

        if (data.EsCandles.Count == 0 || data.NqCandles.Count == 0)
        {
            status = DataConnectionStatus.Error;
            statusMessage ??= "No synchronized ES/NQ candles were available.";
        }
        else if (DateTime.Now - data.EsCandles[^1].Time > TimeSpan.FromMinutes(45))
        {
            status = DataConnectionStatus.Delayed;
            statusMessage = "Latest synchronized candle is delayed.";
        }

        ReplaceMarketData(data);
        DetectSwings();

        MarketDataUpdated?.Invoke(new MarketDataSnapshot(
            EsCandles.ToList(),
            NqCandles.ToList(),
            EsSwings.ToList(),
            NqSwings.ToList(),
            provider,
            status,
            DateTime.Now,
            statusMessage));

        DetectSignals();
    }

    private void ReplaceMarketData(MarketDataSet data)
    {
        EsCandles.Clear();
        EsCandles.AddRange(data.EsCandles);
        NqCandles.Clear();
        NqCandles.AddRange(data.NqCandles);
    }

    private void DetectSwings()
    {
        EsSwings.Clear();
        EsSwings.AddRange(_swingDetector.Detect(EsCandles, Settings.SwingStrength));
        NqSwings.Clear();
        NqSwings.AddRange(_swingDetector.Detect(NqCandles, Settings.SwingStrength));
    }

    private void DetectSignals()
    {
        var signals = _smtDetector.Detect(EsCandles, NqCandles, EsSwings, NqSwings, Settings.DetectionMode);
        foreach (var signal in signals)
        {
            var key = SignalKey(signal);
            if (_emittedSignalKeys.Contains(key))
            {
                continue;
            }

            var pairKey = $"{signal.Type}:{signal.LeaderSymbol}:{signal.FailedSymbol}:{signal.DetectionMode}";
            if (_lastSignalByPair.TryGetValue(pairKey, out var lastSignalTime) &&
                signal.Time - lastSignalTime < Settings.AlertCooldown)
            {
                _emittedSignalKeys.Add(key);
                continue;
            }

            _lastSignalByPair[pairKey] = signal.Time;
            _emittedSignalKeys.Add(key);
            SignalDetected?.Invoke(signal);
            LogGenerated?.Invoke(BuildSignalLog(signal));
        }
    }

    private void ResetState()
    {
        EsCandles.Clear();
        NqCandles.Clear();
        EsSwings.Clear();
        NqSwings.Clear();
        _emittedSignalKeys.Clear();
        _lastSignalByPair.Clear();
    }

    private void SetStatus(AgentStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private static string BuildSignalLog(SmtSignal signal)
    {
        return $"{signal.Time:yyyy-MM-dd HH:mm} | {signal.Type} SMT | Leader: {signal.LeaderSymbol} | Failed: {signal.FailedSymbol} | " +
            $"ES prev {signal.EsPreviousSwingValue:0.00}, current {signal.EsCurrentValue:0.00} | " +
            $"NQ prev {signal.NqPreviousSwingValue:0.00}, current {signal.NqCurrentValue:0.00} | " +
            $"Mode: {signal.DetectionMode} | Timeframe: 15m | {signal.Reason}";
    }

    private static string SignalKey(SmtSignal signal)
    {
        return $"{signal.Time:O}:{signal.Type}:{signal.LeaderSymbol}:{signal.FailedSymbol}:{signal.DetectionMode}";
    }
}
