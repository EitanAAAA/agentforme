using SMTAgent.Core.Models;
using SMTAgent.Core.Engines;

namespace SMTAgent.Api.Infrastructure;

public sealed class AgentRuntime
{
    private readonly YahooFinanceMarketDataProvider _yahooFinanceMarketDataProvider = new();
    private readonly MockMarketDataProvider _mockMarketDataProvider = new();
    private readonly SwingDetector _swingDetector = new();
    private readonly SmtDetector _smtDetector = new();
    private readonly HashSet<string> _announcedSignalIds = [];
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _runTask;

    public event Action<MarketDataSnapshot>? MarketDataUpdated;
    public event Action<SmtSignal>? SignalDetected;
    public event Action<IReadOnlyList<SmtSignal>>? SignalsUpdated;
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
        PublishSnapshot(Settings.DataProvider, DataConnectionStatus.Updating, $"Updating delayed Yahoo {Settings.Timeframe} candles...");

        try
        {
            var data = Settings.DataProvider == DataProviderMode.Mock
                ? _mockMarketDataProvider.Generate(Settings.Timeframe)
                : await _yahooFinanceMarketDataProvider.FetchAsync(Settings.Timeframe, cancellationToken);

            ReplaceMarketData(data);
            DetectSwings();

            var status = GetDataStatus(data);
            var message = Settings.DataProvider == DataProviderMode.Mock
                ? $"Manual demo data - {Settings.Timeframe}. Not market data."
                : $"DELAYED DATA - live updating {Settings.Timeframe} view";

            PublishSnapshot(Settings.DataProvider, status, message);
            DetectSignals();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishSnapshot(Settings.DataProvider, DataConnectionStatus.Error, exception.Message);
        }
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
        var signals = _smtDetector.Detect(EsCandles, NqCandles, EsSwings, NqSwings, Settings);
        SignalsUpdated?.Invoke(signals);

        foreach (var signal in signals)
        {
            if (_announcedSignalIds.Contains(signal.SignalId))
            {
                continue;
            }

            _announcedSignalIds.Add(signal.SignalId);
            SignalDetected?.Invoke(signal);
        }
    }

    private void PublishSnapshot(DataProviderMode provider, DataConnectionStatus status, string? statusMessage)
    {
        MarketDataUpdated?.Invoke(new MarketDataSnapshot(
            EsCandles.ToList(),
            NqCandles.ToList(),
            EsSwings.ToList(),
            NqSwings.ToList(),
            provider,
            status,
            DateTime.Now,
            statusMessage));
    }

    private void ResetState()
    {
        EsCandles.Clear();
        NqCandles.Clear();
        EsSwings.Clear();
        NqSwings.Clear();
        _announcedSignalIds.Clear();
    }

    private void SetStatus(AgentStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private static DataConnectionStatus GetDataStatus(MarketDataSet data)
    {
        if (data.EsCandles.Count == 0 || data.NqCandles.Count == 0)
        {
            return DataConnectionStatus.Error;
        }

        var latest = data.EsCandles[^1].Time;
        return DateTime.Now - latest > TimeSpan.FromMinutes(20)
            ? DataConnectionStatus.Delayed
            : DataConnectionStatus.Connected;
    }

}
