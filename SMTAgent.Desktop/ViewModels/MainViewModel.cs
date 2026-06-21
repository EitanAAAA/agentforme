using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Media;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SMTAgent.Core.Engines;
using SMTAgent.Core.Models;
using Application = System.Windows.Application;

namespace SMTAgent.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AgentRuntime _runtime = new();
    private readonly YahooFinanceMarketDataProvider _focusedDataProvider = new();
    private readonly NqFocusedAnalysisEngine _focusedAnalysisEngine = new();
    private readonly List<SmtSignal> _allSignals = [];
    private readonly DispatcherTimer _focusedRefreshTimer;
    private AgentStatus _status = AgentStatus.Stopped;
    private DataConnectionStatus _dataStatus = DataConnectionStatus.Delayed;
    private string _selectedDataProvider = "Yahoo Delayed";
    private DateTime? _lastUpdated;
    private string? _statusMessage = "DELAYED DATA - live updating view";
    private string _selectedTimeframe = "15m";
    private int _swingStrength = 2;
    private DetectionMode _selectedDetectionMode = DetectionMode.Both;
    private int _refreshRateSeconds = 60;
    private int _alertCooldownMinutes = 2;
    private int _alertCooldownCandles = 4;
    private int _tickTolerance = 1;
    private bool _showBullishSmt = true;
    private bool _showBearishSmt = true;
    private bool _soundAlerts;
    private bool _desktopNotifications;
    private bool _isSettingsOpen;
    private bool _isEsYahooFetchPulseActive;
    private bool _isNqYahooFetchPulseActive;
    private bool _isFocusedNqAnalysisOpen;
    private bool _showBos = true;
    private bool _showFvg = true;
    private bool _showIfvg = true;
    private bool _showHalfBox = true;
    private bool _showSlTp = true;
    private bool _isFocusedAnalysisLoading;
    private SmtSignal? _selectedSmtSignal;
    private FocusedAnalysisResult? _focusedAnalysisResult;
    private string _focusedAnalysisSummary = "Select an SMT event to open NQ 1m analysis.";
    private bool _syncChartViews;
    private bool _syncingChartViews;
    private int _esViewStartIndex;
    private int _nqViewStartIndex;
    private int _esVisibleCandleCount = 90;
    private int _nqVisibleCandleCount = 90;
    private bool _esAutoScrollToLatest = true;
    private bool _nqAutoScrollToLatest = true;
    private DateTime? _esCrosshairTime;
    private DateTime? _nqCrosshairTime;
    private string _esOhlcText = "O -- H -- L -- C --";
    private string _nqOhlcText = "O -- H -- L -- C --";
    private string _esSellText = "--";
    private string _esBuyText = "--";
    private string _nqSellText = "--";
    private string _nqBuyText = "--";

    public MainViewModel()
    {
        StartCommand = new RelayCommand(Start);
        PauseCommand = new RelayCommand(Pause);
        StopCommand = new RelayCommand(Stop);
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        ChartZoomInCommand = new ParameterRelayCommand(parameter => ZoomChart(GetSymbol(parameter), 0.82));
        ChartZoomOutCommand = new ParameterRelayCommand(parameter => ZoomChart(GetSymbol(parameter), 1.18));
        ChartResetViewCommand = new ParameterRelayCommand(parameter => ResetChartView(GetSymbol(parameter)));
        ChartFitDataCommand = new ParameterRelayCommand(parameter => FitChartData(GetSymbol(parameter)));
        ChartGoToLatestCommand = new ParameterRelayCommand(parameter => GoToLatest(GetSymbol(parameter)));
        ToggleSyncChartsCommand = new RelayCommand(() => SyncChartViews = !SyncChartViews);
        SelectTimeframeCommand = new ParameterRelayCommand(SelectTimeframe);
        SelectSignalCommand = new ParameterRelayCommand(parameter => OpenFocusedAnalysis(parameter as SmtSignal));
        CloseFocusedAnalysisCommand = new RelayCommand(CloseFocusedAnalysis);

        _focusedRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _focusedRefreshTimer.Tick += async (_, _) => await RefreshFocusedAnalysisAsync();

        BindRuntimeEvents();
        Application.Current.Dispatcher.BeginInvoke(Start);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Candle> EsCandles { get; } = [];
    public ObservableCollection<Candle> NqCandles { get; } = [];
    public ObservableCollection<SwingPoint> EsSwings { get; } = [];
    public ObservableCollection<SwingPoint> NqSwings { get; } = [];
    public ObservableCollection<SmtSignal> Signals { get; } = [];
    public ObservableCollection<Candle> FocusedNqCandles { get; } = [];
    public ObservableCollection<FocusedChartAnnotation> FocusedNqAnnotations { get; } = [];

    public IReadOnlyList<DetectionMode> DetectionModes { get; } =
        [DetectionMode.WickBreak, DetectionMode.CloseConfirmation, DetectionMode.Both];

    public IReadOnlyList<string> DataProviders { get; } =
        ["Yahoo Delayed", "Manual Demo"];

    public IReadOnlyList<string> Timeframes { get; } =
        ["30s", "1m", "5m", "15m"];

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand ChartZoomInCommand { get; }
    public ICommand ChartZoomOutCommand { get; }
    public ICommand ChartResetViewCommand { get; }
    public ICommand ChartFitDataCommand { get; }
    public ICommand ChartGoToLatestCommand { get; }
    public ICommand ToggleSyncChartsCommand { get; }
    public ICommand SelectTimeframeCommand { get; }
    public ICommand SelectSignalCommand { get; }
    public ICommand CloseFocusedAnalysisCommand { get; }

    public AgentStatus Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => Status.ToString();

    public DataConnectionStatus DataStatus
    {
        get => _dataStatus;
        private set
        {
            if (SetField(ref _dataStatus, value))
            {
                OnPropertyChanged(nameof(DataStatusText));
            }
        }
    }

    public string DataStatusText => DataStatus.ToString();
    public string TimeframeText => SelectedTimeframe;

    public string SelectedTimeframe
    {
        get => _selectedTimeframe;
        set
        {
            var normalized = NormalizeTimeframe(value);
            if (SetField(ref _selectedTimeframe, normalized))
            {
                _runtime.Settings.Timeframe = normalized;
                OnPropertyChanged(nameof(TimeframeText));
                StatusMessage = BuildStatusMessage();

                if (Status == AgentStatus.Running && !IsFocusedNqAnalysisOpen)
                {
                    Start();
                }
            }
        }
    }

    public string EsOhlcText
    {
        get => _esOhlcText;
        private set => SetField(ref _esOhlcText, value);
    }

    public string NqOhlcText
    {
        get => _nqOhlcText;
        private set => SetField(ref _nqOhlcText, value);
    }

    public string EsSellText
    {
        get => _esSellText;
        private set => SetField(ref _esSellText, value);
    }

    public string EsBuyText
    {
        get => _esBuyText;
        private set => SetField(ref _esBuyText, value);
    }

    public string NqSellText
    {
        get => _nqSellText;
        private set => SetField(ref _nqSellText, value);
    }

    public string NqBuyText
    {
        get => _nqBuyText;
        private set => SetField(ref _nqBuyText, value);
    }

    public DateTime? LastUpdated
    {
        get => _lastUpdated;
        private set
        {
            if (SetField(ref _lastUpdated, value))
            {
                OnPropertyChanged(nameof(LastUpdatedText));
            }
        }
    }

    public string LastUpdatedText => LastUpdated is null ? "Last update --" : $"Last update {LastUpdated:HH:mm:ss}";

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetField(ref _isSettingsOpen, value);
    }

    public bool IsFocusedNqAnalysisOpen
    {
        get => _isFocusedNqAnalysisOpen;
        private set
        {
            if (SetField(ref _isFocusedNqAnalysisOpen, value))
            {
                OnPropertyChanged(nameof(IsComparisonDashboardVisible));
            }
        }
    }

    public bool IsComparisonDashboardVisible => !IsFocusedNqAnalysisOpen;

    public bool IsFocusedAnalysisLoading
    {
        get => _isFocusedAnalysisLoading;
        private set => SetField(ref _isFocusedAnalysisLoading, value);
    }

    public SmtSignal? SelectedSmtSignal
    {
        get => _selectedSmtSignal;
        private set
        {
            if (SetField(ref _selectedSmtSignal, value))
            {
                OnPropertyChanged(nameof(FocusedTitle));
            }
        }
    }

    public string FocusedTitle => SelectedSmtSignal is null
        ? "NQ 1m Focus"
        : $"NQ 1m Focus - {SelectedSmtSignal.Type} {SelectedSmtSignal.SetupType}";

    public string FocusedAnalysisSummary
    {
        get => _focusedAnalysisSummary;
        private set => SetField(ref _focusedAnalysisSummary, value);
    }

    public bool ShowBos
    {
        get => _showBos;
        set
        {
            if (SetField(ref _showBos, value))
            {
                RebuildFocusedAnnotations();
            }
        }
    }

    public bool ShowFvg
    {
        get => _showFvg;
        set
        {
            if (SetField(ref _showFvg, value))
            {
                RebuildFocusedAnnotations();
            }
        }
    }

    public bool ShowIfvg
    {
        get => _showIfvg;
        set
        {
            if (SetField(ref _showIfvg, value))
            {
                RebuildFocusedAnnotations();
            }
        }
    }

    public bool ShowHalfBox
    {
        get => _showHalfBox;
        set
        {
            if (SetField(ref _showHalfBox, value))
            {
                RebuildFocusedAnnotations();
            }
        }
    }

    public bool ShowSlTp
    {
        get => _showSlTp;
        set
        {
            if (SetField(ref _showSlTp, value))
            {
                RebuildFocusedAnnotations();
            }
        }
    }

    public bool SyncChartViews
    {
        get => _syncChartViews;
        set
        {
            if (SetField(ref _syncChartViews, value))
            {
                OnPropertyChanged(nameof(SyncChartViewsText));
                if (value)
                {
                    SyncFromEs();
                }
            }
        }
    }

    public string SyncChartViewsText => SyncChartViews ? "Sync On" : "Sync Off";

    public int EsViewStartIndex
    {
        get => _esViewStartIndex;
        set
        {
            if (SetField(ref _esViewStartIndex, Math.Max(0, value)) && SyncChartViews && !_syncingChartViews)
            {
                SyncFromEs();
            }
        }
    }

    public int NqViewStartIndex
    {
        get => _nqViewStartIndex;
        set
        {
            if (SetField(ref _nqViewStartIndex, Math.Max(0, value)) && SyncChartViews && !_syncingChartViews)
            {
                SyncFromNq();
            }
        }
    }

    public int EsVisibleCandleCount
    {
        get => _esVisibleCandleCount;
        set
        {
            if (SetField(ref _esVisibleCandleCount, Math.Max(12, value)) && SyncChartViews && !_syncingChartViews)
            {
                SyncFromEs();
            }
        }
    }

    public int NqVisibleCandleCount
    {
        get => _nqVisibleCandleCount;
        set
        {
            if (SetField(ref _nqVisibleCandleCount, Math.Max(12, value)) && SyncChartViews && !_syncingChartViews)
            {
                SyncFromNq();
            }
        }
    }

    public bool EsAutoScrollToLatest
    {
        get => _esAutoScrollToLatest;
        set
        {
            if (SetField(ref _esAutoScrollToLatest, value) && SyncChartViews && !_syncingChartViews)
            {
                SyncFromEs();
            }
        }
    }

    public bool NqAutoScrollToLatest
    {
        get => _nqAutoScrollToLatest;
        set
        {
            if (SetField(ref _nqAutoScrollToLatest, value) && SyncChartViews && !_syncingChartViews)
            {
                SyncFromNq();
            }
        }
    }

    public DateTime? EsCrosshairTime
    {
        get => _esCrosshairTime;
        set
        {
            if (SetField(ref _esCrosshairTime, value) && SyncChartViews && !_syncingChartViews)
            {
                SyncFromEs();
            }
        }
    }

    public DateTime? NqCrosshairTime
    {
        get => _nqCrosshairTime;
        set
        {
            if (SetField(ref _nqCrosshairTime, value) && SyncChartViews && !_syncingChartViews)
            {
                SyncFromNq();
            }
        }
    }

    public string SelectedDataProvider
    {
        get => _selectedDataProvider;
        set
        {
            if (SetField(ref _selectedDataProvider, value))
            {
                _runtime.Settings.DataProvider = SelectedProviderMode;
                OnPropertyChanged(nameof(DataProviderText));
                StatusMessage = BuildStatusMessage();
                IsEsYahooFetchPulseActive = false;
                IsNqYahooFetchPulseActive = false;
            }
        }
    }

    public string DataProviderText => SelectedDataProvider;
    private DataProviderMode SelectedProviderMode => SelectedDataProvider == "Manual Demo"
        ? DataProviderMode.Mock
        : DataProviderMode.YahooFinance;

    public bool IsEsYahooFetchPulseActive
    {
        get => _isEsYahooFetchPulseActive;
        private set => SetField(ref _isEsYahooFetchPulseActive, value);
    }

    public bool IsNqYahooFetchPulseActive
    {
        get => _isNqYahooFetchPulseActive;
        private set => SetField(ref _isNqYahooFetchPulseActive, value);
    }

    public int SwingStrength
    {
        get => _swingStrength;
        set
        {
            var next = Math.Clamp(value, 1, 6);
            if (SetField(ref _swingStrength, next))
            {
                _runtime.Settings.SwingStrength = next;
            }
        }
    }

    public DetectionMode SelectedDetectionMode
    {
        get => _selectedDetectionMode;
        set
        {
            if (SetField(ref _selectedDetectionMode, value))
            {
                _runtime.Settings.DetectionMode = value;
            }
        }
    }

    public int RefreshRateSeconds
    {
        get => _refreshRateSeconds;
        set
        {
            var next = Math.Clamp(value, 15, 300);
            if (SetField(ref _refreshRateSeconds, next))
            {
                _runtime.Settings.RefreshRate = TimeSpan.FromSeconds(next);
            }
        }
    }

    public int AlertCooldownMinutes
    {
        get => _alertCooldownMinutes;
        set
        {
            var next = Math.Clamp(value, 0, 30);
            if (SetField(ref _alertCooldownMinutes, next))
            {
                _runtime.Settings.AlertCooldown = TimeSpan.FromMinutes(next);
            }
        }
    }

    public int AlertCooldownCandles
    {
        get => _alertCooldownCandles;
        set
        {
            var next = Math.Clamp(value, 0, 50);
            if (SetField(ref _alertCooldownCandles, next))
            {
                _runtime.Settings.AlertCooldownCandles = next;
            }
        }
    }

    public int TickTolerance
    {
        get => _tickTolerance;
        set
        {
            var next = Math.Clamp(value, 0, 10);
            if (SetField(ref _tickTolerance, next))
            {
                _runtime.Settings.TickTolerance = next;
            }
        }
    }

    public bool ShowBullishSmt
    {
        get => _showBullishSmt;
        set
        {
            if (SetField(ref _showBullishSmt, value))
            {
                _runtime.Settings.ShowBullishSmt = value;
                RebuildVisibleSignals();
            }
        }
    }

    public bool ShowBearishSmt
    {
        get => _showBearishSmt;
        set
        {
            if (SetField(ref _showBearishSmt, value))
            {
                _runtime.Settings.ShowBearishSmt = value;
                RebuildVisibleSignals();
            }
        }
    }

    public bool SoundAlerts
    {
        get => _soundAlerts;
        set
        {
            if (SetField(ref _soundAlerts, value))
            {
                _runtime.Settings.SoundAlerts = value;
            }
        }
    }

    public bool DesktopNotifications
    {
        get => _desktopNotifications;
        set
        {
            if (SetField(ref _desktopNotifications, value))
            {
                _runtime.Settings.DesktopNotifications = value;
            }
        }
    }

    private void Start()
    {
        ClearVisualState();
        _runtime.Settings.Timeframe = SelectedTimeframe;
        _runtime.Settings.SwingStrength = SwingStrength;
        _runtime.Settings.DetectionMode = SelectedDetectionMode;
        _runtime.Settings.RefreshRate = TimeSpan.FromSeconds(RefreshRateSeconds);
        _runtime.Settings.AlertCooldown = TimeSpan.FromMinutes(AlertCooldownMinutes);
        _runtime.Settings.AlertCooldownCandles = AlertCooldownCandles;
        _runtime.Settings.TickTolerance = TickTolerance;
        _runtime.Settings.ShowBullishSmt = ShowBullishSmt;
        _runtime.Settings.ShowBearishSmt = ShowBearishSmt;
        _runtime.Settings.SoundAlerts = SoundAlerts;
        _runtime.Settings.DesktopNotifications = DesktopNotifications;
        _runtime.Settings.DataProvider = SelectedProviderMode;
        GoToLatest("ES");
        GoToLatest("NQ");
        _runtime.Start();
    }

    private void Pause()
    {
        _runtime.Pause();
    }

    private void Stop()
    {
        _runtime.Stop();
    }

    private void BindRuntimeEvents()
    {
        _runtime.MarketDataUpdated += snapshot => OnUi(() =>
        {
            Replace(EsCandles, snapshot.EsCandles);
            Replace(NqCandles, snapshot.NqCandles);
            Replace(EsSwings, snapshot.EsSwings);
            Replace(NqSwings, snapshot.NqSwings);
            DataStatus = snapshot.DataStatus;
            LastUpdated = snapshot.LastUpdated;
            StatusMessage = snapshot.StatusMessage;
            UpdateQuoteHeaders();
            PulseYahooChartBorders(snapshot);
        });

        _runtime.SignalsUpdated += signals => OnUi(() =>
        {
            _allSignals.Clear();
            _allSignals.AddRange(signals);
            RebuildVisibleSignals();
        });

        _runtime.SignalDetected += signal => OnUi(() =>
        {
            Alert(signal);
        });

        _runtime.StatusChanged += status => OnUi(() => Status = status);
    }

    private void Alert(SmtSignal signal)
    {
        if (SoundAlerts)
        {
            SystemSounds.Asterisk.Play();
        }

        if (DesktopNotifications)
        {
            StatusMessage = $"{signal.Time:HH:mm} | {signal.Reason}";
        }
    }

    private void ClearVisualState()
    {
        EsCandles.Clear();
        NqCandles.Clear();
        EsSwings.Clear();
        NqSwings.Clear();
        Signals.Clear();
        _allSignals.Clear();
        StatusMessage = BuildStatusMessage();
        EsOhlcText = "O -- H -- L -- C --";
        NqOhlcText = "O -- H -- L -- C --";
        EsSellText = "--";
        EsBuyText = "--";
        NqSellText = "--";
        NqBuyText = "--";
        IsEsYahooFetchPulseActive = false;
        IsNqYahooFetchPulseActive = false;
    }

    private void UpdateQuoteHeaders()
    {
        UpdateQuoteHeader(EsCandles, 0.25m, value => EsOhlcText = value, value => EsSellText = value, value => EsBuyText = value);
        UpdateQuoteHeader(NqCandles, 0.25m, value => NqOhlcText = value, value => NqSellText = value, value => NqBuyText = value);
    }

    private static void UpdateQuoteHeader(
        IList<Candle> candles,
        decimal tickSize,
        Action<string> setOhlc,
        Action<string> setSell,
        Action<string> setBuy)
    {
        if (candles.Count == 0)
        {
            setOhlc("O -- H -- L -- C --");
            setSell("--");
            setBuy("--");
            return;
        }

        var candle = candles[^1];
        var previousClose = candles.Count > 1 ? candles[^2].Close : candle.Open;
        var change = candle.Close - previousClose;
        var changePercent = previousClose == 0 ? 0 : change / previousClose * 100m;
        var sign = change >= 0 ? "+" : string.Empty;
        setOhlc($"O {candle.Open:N2}  H {candle.High:N2}  L {candle.Low:N2}  C {candle.Close:N2}  {sign}{change:N2} ({sign}{changePercent:N2}%)");
        setSell($"{candle.Close - tickSize:N2}");
        setBuy($"{candle.Close + tickSize:N2}");
    }

    private async void PulseYahooChartBorders(MarketDataSnapshot snapshot)
    {
        if (snapshot.DataProvider != DataProviderMode.YahooFinance || snapshot.DataStatus == DataConnectionStatus.Error)
        {
            IsEsYahooFetchPulseActive = false;
            IsNqYahooFetchPulseActive = false;
            return;
        }

        var pulseEs = snapshot.EsCandles.Count > 0;
        var pulseNq = snapshot.NqCandles.Count > 0;
        if (!pulseEs && !pulseNq)
        {
            return;
        }

        IsEsYahooFetchPulseActive = pulseEs;
        IsNqYahooFetchPulseActive = pulseNq;
        await Task.Delay(1500);
        IsEsYahooFetchPulseActive = false;
        IsNqYahooFetchPulseActive = false;
    }

    private void SelectTimeframe(object? parameter)
    {
        SelectedTimeframe = parameter?.ToString() ?? SelectedTimeframe;
    }

    private async void OpenFocusedAnalysis(SmtSignal? signal)
    {
        if (signal is null)
        {
            return;
        }

        SelectedSmtSignal = signal;
        IsFocusedNqAnalysisOpen = true;
        SelectedTimeframe = "1m";
        StatusMessage = $"Opening NQ 1m analysis for {signal.Time:HH:mm} | {signal.Reason}";
        _focusedRefreshTimer.Stop();
        await RefreshFocusedAnalysisAsync();
        _focusedRefreshTimer.Start();
    }

    private async Task RefreshFocusedAnalysisAsync()
    {
        if (SelectedSmtSignal is null)
        {
            return;
        }

        IsFocusedAnalysisLoading = true;
        try
        {
            var candles = await _focusedDataProvider.FetchNqAsync("1m", CancellationToken.None);
            if (candles.Count == 0)
            {
                FocusedAnalysisSummary = "Yahoo returned no NQ 1m candles for this focused view.";
                DataStatus = DataConnectionStatus.Error;
                return;
            }

            var result = _focusedAnalysisEngine.Analyze(candles, SelectedSmtSignal, _runtime.Settings);
            _focusedAnalysisResult = result;
            var focusedCandles = candles
                .Where(candle => candle.Time >= result.WindowStart && candle.Time <= result.WindowEnd)
                .OrderBy(candle => candle.Time)
                .ToList();

            if (focusedCandles.Count == 0)
            {
                focusedCandles = candles
                    .Where(candle => candle.Time >= SelectedSmtSignal.Time.AddMinutes(-10) && candle.Time <= SelectedSmtSignal.Time.AddMinutes(10))
                    .OrderBy(candle => candle.Time)
                    .ToList();
            }

            Replace(FocusedNqCandles, focusedCandles);
            FocusedAnalysisSummary = result.Summary;
            RebuildFocusedAnnotations();
            NqViewStartIndex = 0;
            NqVisibleCandleCount = Math.Max(12, FocusedNqCandles.Count);
            NqAutoScrollToLatest = false;
            NqCrosshairTime = SelectedSmtSignal.Time;
            UpdateQuoteHeader(FocusedNqCandles, 0.25m, value => NqOhlcText = value, value => NqSellText = value, value => NqBuyText = value);
            DataStatus = DataConnectionStatus.Delayed;
            LastUpdated = DateTime.Now;
            StatusMessage = $"NQ 1m focused analysis refreshed | {SelectedSmtSignal.Time:HH:mm}";
        }
        catch (Exception exception)
        {
            DataStatus = DataConnectionStatus.Error;
            FocusedAnalysisSummary = $"Focused NQ 1m refresh failed: {exception.Message}";
            StatusMessage = FocusedAnalysisSummary;
        }
        finally
        {
            IsFocusedAnalysisLoading = false;
        }
    }

    private void CloseFocusedAnalysis()
    {
        _focusedRefreshTimer.Stop();
        IsFocusedNqAnalysisOpen = false;
        SelectedSmtSignal = null;
        _focusedAnalysisResult = null;
        FocusedNqCandles.Clear();
        FocusedNqAnnotations.Clear();
        FocusedAnalysisSummary = "Select an SMT event to open NQ 1m analysis.";
        UpdateQuoteHeaders();
        StatusMessage = BuildStatusMessage();
    }

    private void RebuildFocusedAnnotations()
    {
        FocusedNqAnnotations.Clear();
        if (_focusedAnalysisResult is null)
        {
            return;
        }

        foreach (var annotation in _focusedAnalysisResult.Annotations.Where(IsFocusedAnnotationVisible))
        {
            FocusedNqAnnotations.Add(annotation);
        }
    }

    private bool IsFocusedAnnotationVisible(FocusedChartAnnotation annotation)
    {
        return annotation.Kind switch
        {
            FocusedAnnotationKind.Bos => ShowBos,
            FocusedAnnotationKind.Fvg => ShowFvg,
            FocusedAnnotationKind.Ifvg => ShowIfvg,
            FocusedAnnotationKind.HalfBox => ShowHalfBox,
            FocusedAnnotationKind.StopTakeProfit => ShowSlTp,
            _ => true
        };
    }

    private void ZoomChart(string symbol, double factor)
    {
        var candleCount = GetCandleCount(symbol);
        if (candleCount == 0)
        {
            return;
        }

        var start = GetViewStart(symbol);
        var visible = NormalizeVisible(GetVisibleCount(symbol), candleCount);
        var center = start + (visible / 2.0);
        var nextVisible = NormalizeVisible((int)Math.Round(visible * factor), candleCount);
        var nextStart = (int)Math.Round(center - (nextVisible / 2.0));
        SetChartViewport(symbol, nextStart, nextVisible, false);
    }

    private void ResetChartView(string symbol)
    {
        var candleCount = GetCandleCount(symbol);
        var visible = NormalizeVisible(90, candleCount);
        SetChartViewport(symbol, Math.Max(0, candleCount - visible), visible, true);
    }

    private void FitChartData(string symbol)
    {
        var candleCount = GetCandleCount(symbol);
        if (candleCount == 0)
        {
            return;
        }

        SetChartViewport(symbol, 0, candleCount, false);
    }

    private void GoToLatest(string symbol)
    {
        var candleCount = GetCandleCount(symbol);
        if (candleCount == 0)
        {
            EsAutoScrollToLatest = true;
            NqAutoScrollToLatest = true;
            return;
        }

        var visible = NormalizeVisible(GetVisibleCount(symbol), candleCount);
        SetChartViewport(symbol, Math.Max(0, candleCount - visible), visible, true);
    }

    private void SetChartViewport(string symbol, int start, int visible, bool autoScroll)
    {
        var candleCount = GetCandleCount(symbol);
        if (candleCount > 0)
        {
            visible = NormalizeVisible(visible, candleCount);
            start = Math.Clamp(start, 0, Math.Max(0, candleCount - visible));
        }

        if (symbol == "NQ")
        {
            NqVisibleCandleCount = visible;
            NqViewStartIndex = start;
            NqAutoScrollToLatest = autoScroll;
            return;
        }

        EsVisibleCandleCount = visible;
        EsViewStartIndex = start;
        EsAutoScrollToLatest = autoScroll;
    }

    private int GetCandleCount(string symbol)
    {
        return symbol == "NQ" ? NqCandles.Count : EsCandles.Count;
    }

    private int GetViewStart(string symbol)
    {
        return symbol == "NQ" ? NqViewStartIndex : EsViewStartIndex;
    }

    private int GetVisibleCount(string symbol)
    {
        return symbol == "NQ" ? NqVisibleCandleCount : EsVisibleCandleCount;
    }

    private void SyncFromEs()
    {
        _syncingChartViews = true;
        NqViewStartIndex = EsViewStartIndex;
        NqVisibleCandleCount = EsVisibleCandleCount;
        NqAutoScrollToLatest = EsAutoScrollToLatest;
        NqCrosshairTime = EsCrosshairTime;
        _syncingChartViews = false;
    }

    private void SyncFromNq()
    {
        _syncingChartViews = true;
        EsViewStartIndex = NqViewStartIndex;
        EsVisibleCandleCount = NqVisibleCandleCount;
        EsAutoScrollToLatest = NqAutoScrollToLatest;
        EsCrosshairTime = NqCrosshairTime;
        _syncingChartViews = false;
    }

    private static string GetSymbol(object? parameter)
    {
        return string.Equals(parameter?.ToString(), "NQ", StringComparison.OrdinalIgnoreCase) ? "NQ" : "ES";
    }

    private static string NormalizeTimeframe(string? timeframe)
    {
        return timeframe switch
        {
            "30s" or "1m" or "5m" or "15m" => timeframe,
            _ => "15m"
        };
    }

    private string BuildStatusMessage()
    {
        return SelectedProviderMode == DataProviderMode.Mock
            ? $"Manual demo data - {SelectedTimeframe}. Not market data."
            : $"DELAYED DATA - live updating {SelectedTimeframe} view";
    }

    private static int NormalizeVisible(int requested, int candleCount)
    {
        if (candleCount <= 0)
        {
            return Math.Max(12, requested);
        }

        var minimum = Math.Min(12, candleCount);
        return Math.Clamp(requested, minimum, candleCount);
    }

    private void RebuildVisibleSignals()
    {
        Signals.Clear();
        foreach (var signal in _allSignals.Where(IsSignalVisible).OrderByDescending(signal => signal.Time))
        {
            Signals.Add(signal);
        }
    }

    private bool IsSignalVisible(SmtSignal signal)
    {
        return signal.Type switch
        {
            SmtSignalType.Bullish => ShowBullishSmt,
            SmtSignalType.Bearish => ShowBearishSmt,
            _ => true
        };
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
