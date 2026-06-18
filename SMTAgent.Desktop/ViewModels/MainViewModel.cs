using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SMTAgent.Core.Engines;
using SMTAgent.Core.Models;

namespace SMTAgent.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AgentRuntime _runtime = new();
    private readonly List<SmtSignal> _allSignals = [];
    private AgentStatus _status = AgentStatus.Stopped;
    private DataConnectionStatus _dataStatus = DataConnectionStatus.Delayed;
    private DataProviderMode _selectedDataProvider = DataProviderMode.YahooFinance;
    private DateTime? _lastUpdated;
    private string? _statusMessage;
    private int _swingStrength = 2;
    private DetectionMode _selectedDetectionMode = DetectionMode.Both;
    private int _refreshRateSeconds = 60;
    private int _alertCooldownMinutes = 2;
    private bool _showBullishSmt = true;
    private bool _showBearishSmt = true;

    public MainViewModel()
    {
        StartCommand = new RelayCommand(Start);
        PauseCommand = new RelayCommand(Pause);
        StopCommand = new RelayCommand(Stop);

        BindRuntimeEvents();
        Application.Current.Dispatcher.BeginInvoke(Start);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Candle> EsCandles { get; } = [];
    public ObservableCollection<Candle> NqCandles { get; } = [];
    public ObservableCollection<SwingPoint> EsSwings { get; } = [];
    public ObservableCollection<SwingPoint> NqSwings { get; } = [];
    public ObservableCollection<SmtSignal> Signals { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];

    public IReadOnlyList<DetectionMode> DetectionModes { get; } =
        [DetectionMode.WickBreak, DetectionMode.CloseConfirmation, DetectionMode.Both];

    public IReadOnlyList<DataProviderMode> DataProviders { get; } =
        [DataProviderMode.YahooFinance, DataProviderMode.Mock];

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }

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

    public string LastUpdatedText => LastUpdated is null ? "Last Updated: --" : $"Last Updated: {LastUpdated:HH:mm:ss}";

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public DataProviderMode SelectedDataProvider
    {
        get => _selectedDataProvider;
        set
        {
            if (SetField(ref _selectedDataProvider, value))
            {
                _runtime.Settings.DataProvider = value;
                OnPropertyChanged(nameof(DataProviderText));
            }
        }
    }

    public string DataProviderText => SelectedDataProvider == DataProviderMode.YahooFinance ? "Yahoo Finance" : "Mock";

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

    private void Start()
    {
        ClearVisualState();
        _runtime.Settings.Timeframe = "15m";
        _runtime.Settings.SwingStrength = SwingStrength;
        _runtime.Settings.DetectionMode = SelectedDetectionMode;
        _runtime.Settings.RefreshRate = TimeSpan.FromSeconds(RefreshRateSeconds);
        _runtime.Settings.AlertCooldown = TimeSpan.FromMinutes(AlertCooldownMinutes);
        _runtime.Settings.ShowBullishSmt = ShowBullishSmt;
        _runtime.Settings.ShowBearishSmt = ShowBearishSmt;
        _runtime.Settings.DataProvider = SelectedDataProvider;
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

            if (snapshot.DataProvider != SelectedDataProvider)
            {
                _selectedDataProvider = snapshot.DataProvider;
                OnPropertyChanged(nameof(SelectedDataProvider));
                OnPropertyChanged(nameof(DataProviderText));
            }
        });

        _runtime.SignalDetected += signal => OnUi(() =>
        {
            _allSignals.Add(signal);
            if (IsSignalVisible(signal))
            {
                Signals.Insert(0, signal);
            }
        });

        _runtime.LogGenerated += log => OnUi(() =>
        {
            Logs.Insert(0, log);
            if (Logs.Count > 200)
            {
                Logs.RemoveAt(Logs.Count - 1);
            }
        });

        _runtime.StatusChanged += status => OnUi(() => Status = status);
    }

    private void ClearVisualState()
    {
        EsCandles.Clear();
        NqCandles.Clear();
        EsSwings.Clear();
        NqSwings.Clear();
        Signals.Clear();
        Logs.Clear();
        _allSignals.Clear();
        StatusMessage = null;
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
