using Microsoft.AspNetCore.SignalR;
using SMTAgent.Api.Contracts;
using SMTAgent.Api.Hubs;
using SMTAgent.Api.Infrastructure;
using SMTAgent.Api.Mapping;
using SMTAgent.Core.Engines;
using SMTAgent.Core.Models;

namespace SMTAgent.Api.Services;

public sealed class MarketRuntimeService : IHostedService
{
    private readonly AgentRuntime _runtime = new();
    private readonly YahooFinanceMarketDataProvider _focusedDataProvider = new();
    private readonly NqFocusedAnalysisEngine _focusedAnalysisEngine = new();
    private readonly IHubContext<MarketHub> _hubContext;
    private readonly object _gate = new();
    private List<Candle> _esCandles = [];
    private List<Candle> _nqCandles = [];
    private List<SwingPoint> _esSwings = [];
    private List<SwingPoint> _nqSwings = [];
    private List<SmtSignal> _smtEvents = [];
    private AgentStatus _status = AgentStatus.Stopped;
    private DataConnectionStatus _dataStatus = DataConnectionStatus.Delayed;
    private DateTime? _lastUpdated;
    private string _statusMessage = "DELAYED DATA - live updating view";

    public MarketRuntimeService(IHubContext<MarketHub> hubContext)
    {
        _hubContext = hubContext;
        _runtime.Settings.Timeframe = "15m";
        _runtime.MarketDataUpdated += OnMarketDataUpdated;
        _runtime.SignalsUpdated += OnSignalsUpdated;
        _runtime.SignalDetected += OnSignalDetected;
        _runtime.StatusChanged += OnStatusChanged;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runtime.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _runtime.Stop();
        return Task.CompletedTask;
    }

    public DataStatusDto GetStatus()
    {
        lock (_gate)
        {
            return SMTAgent.Api.Mapping.ApiMapping.ToStatusDto(_status, _dataStatus, _runtime.Settings.DataProvider, _lastUpdated, _statusMessage);
        }
    }

    public AppSettingsDto GetSettings()
    {
        lock (_gate)
        {
            return _runtime.Settings.ToDto();
        }
    }

    public AppSettingsDto UpdateSettings(AppSettingsDto settings)
    {
        lock (_gate)
        {
            _runtime.Settings.Apply(settings);
            _statusMessage = BuildStatusMessage();
        }

        _runtime.Start();
        _ = BroadcastAsync("DataStatusChanged", GetStatus());
        return GetSettings();
    }

    public IReadOnlyList<CandleDto> GetCandles(string symbol, string timeframe)
    {
        lock (_gate)
        {
            var candles = symbol.Equals("NQ", StringComparison.OrdinalIgnoreCase) ? _nqCandles : _esCandles;
            return candles.Select(candle => candle.ToDto()).ToList();
        }
    }

    public IReadOnlyList<SwingPointDto> GetSwings(string symbol)
    {
        lock (_gate)
        {
            var swings = symbol.Equals("NQ", StringComparison.OrdinalIgnoreCase) ? _nqSwings : _esSwings;
            return swings.Select(swing => swing.ToDto()).ToList();
        }
    }

    public IReadOnlyList<SmtEventDto> GetSmtEvents()
    {
        lock (_gate)
        {
            return _smtEvents
                .Where(IsWithinHistoryWindow)
                .OrderByDescending(signal => signal.Time)
                .Select(signal => signal.ToDto())
                .ToList();
        }
    }

    public SmtEventDto? GetSmtEvent(string id)
    {
        lock (_gate)
        {
            return _smtEvents.FirstOrDefault(signal => signal.SignalId == id)?.ToDto();
        }
    }

    public async Task<NqOneMinuteAnalysisDto?> GetFocusedAnalysisAsync(string id, CancellationToken cancellationToken)
    {
        SmtSignal? signal;
        lock (_gate)
        {
            signal = _smtEvents.FirstOrDefault(item => item.SignalId == id);
        }

        if (signal is null)
        {
            return null;
        }

        var candles = await _focusedDataProvider.FetchNqAsync("1m", cancellationToken);
        var result = _focusedAnalysisEngine.Analyze(candles, signal, _runtime.Settings);
        var focusedCandles = candles
            .Where(candle => candle.Time >= result.WindowStart && candle.Time <= result.WindowEnd)
            .OrderBy(candle => candle.Time)
            .ToList();

        var dto = result.ToDto(signal.SignalId, focusedCandles);
        _ = BroadcastAsync("FocusedAnalysisUpdated", dto);
        return dto;
    }

    private void OnMarketDataUpdated(MarketDataSnapshot snapshot)
    {
        lock (_gate)
        {
            _esCandles = snapshot.EsCandles.ToList();
            _nqCandles = snapshot.NqCandles.ToList();
            _esSwings = snapshot.EsSwings.ToList();
            _nqSwings = snapshot.NqSwings.ToList();
            _dataStatus = snapshot.DataStatus;
            _lastUpdated = snapshot.LastUpdated;
            _statusMessage = snapshot.StatusMessage ?? BuildStatusMessage();
        }

        var esCandles = snapshot.EsCandles.Select(candle => candle.ToDto()).ToList();
        var nqCandles = snapshot.NqCandles.Select(candle => candle.ToDto()).ToList();
        _ = BroadcastAsync("DataStatusChanged", GetStatus());
        _ = BroadcastAsync("CandlesUpdated", new { symbol = "ES", timeframe = _runtime.Settings.Timeframe, candles = esCandles });
        _ = BroadcastAsync("CandlesUpdated", new { symbol = "NQ", timeframe = _runtime.Settings.Timeframe, candles = nqCandles });

        if (esCandles.Count > 0)
        {
            _ = BroadcastAsync("LatestCandleUpdated", new { symbol = "ES", timeframe = _runtime.Settings.Timeframe, candle = esCandles[^1] });
        }

        if (nqCandles.Count > 0)
        {
            _ = BroadcastAsync("LatestCandleUpdated", new { symbol = "NQ", timeframe = _runtime.Settings.Timeframe, candle = nqCandles[^1] });
        }
    }

    private void OnSignalsUpdated(IReadOnlyList<SmtSignal> signals)
    {
        List<SmtEventCanceledDto> canceledEvents;
        List<SmtEventDto> eventDtos;
        lock (_gate)
        {
            var nextIds = signals.Select(signal => signal.SignalId).ToHashSet(StringComparer.Ordinal);
            var newlyCanceled = _smtEvents
                .Where(signal => signal.Status != SmtSignalStatus.Canceled && !nextIds.Contains(signal.SignalId))
                .Select(MarkCanceled)
                .ToList();
            var retainedCanceled = _smtEvents
                .Where(signal => signal.Status == SmtSignalStatus.Canceled && !nextIds.Contains(signal.SignalId))
                .ToList();
            canceledEvents = newlyCanceled
                .Select(signal => new SmtEventCanceledDto(signal.SignalId, signal.WorkflowState))
                .ToList();
            _smtEvents = signals
                .Concat(retainedCanceled)
                .Concat(newlyCanceled)
                .GroupBy(signal => signal.SignalId, StringComparer.Ordinal)
                .Select(group => group.First())
                .Where(IsWithinHistoryWindow)
                .ToList();
            eventDtos = _smtEvents
                .OrderByDescending(signal => signal.Time)
                .Select(signal => signal.ToDto())
                .ToList();
        }

        _ = BroadcastAsync("SmtEventsUpdated", eventDtos);
        foreach (var canceled in canceledEvents)
        {
            _ = BroadcastAsync("SmtEventCanceled", canceled);
        }
    }

    private void OnSignalDetected(SmtSignal signal)
    {
        _ = BroadcastAsync("SmtEventUpdated", signal.ToDto());
    }

    private void OnStatusChanged(AgentStatus status)
    {
        lock (_gate)
        {
            _status = status;
        }

        _ = BroadcastAsync("DataStatusChanged", GetStatus());
    }

    private string BuildStatusMessage()
    {
        return _runtime.Settings.DataProvider == DataProviderMode.Mock
            ? $"Manual demo data - {_runtime.Settings.Timeframe}. Not market data."
            : $"DELAYED DATA - live updating {_runtime.Settings.Timeframe} view";
    }

    private static string BuildCanceledMessage(SmtSignal signal)
    {
        var side = signal.Type == SmtSignalType.Bearish ? "high" : "low";
        return $"SMT canceled: {signal.FailedSymbol} reached {side}";
    }

    private static SmtSignal MarkCanceled(SmtSignal signal)
    {
        return signal with
        {
            Status = SmtSignalStatus.Canceled,
            WorkflowState = BuildCanceledMessage(signal)
        };
    }

    private bool IsWithinHistoryWindow(SmtSignal signal)
    {
        var latest = _esCandles
            .Concat(_nqCandles)
            .Select(candle => (DateTime?)candle.Time)
            .Max();
        var cutoff = (latest ?? DateTime.Now) - TimeSpan.FromHours(24);
        return signal.Time >= cutoff;
    }

    private async Task BroadcastAsync(string eventName, object payload)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync(eventName, payload);
        }
        catch
        {
            // Local dashboard updates are best-effort; REST remains the source of truth.
        }
    }
}
