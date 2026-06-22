namespace SMTAgent.Core.Models;

public enum SwingPointType
{
    High,
    Low
}

public enum SmtSignalType
{
    Bullish,
    Bearish
}

public enum SmtSetupType
{
    HighLow,
    Fvg,
    InvertedFvg
}

public enum SmtSignalStatus
{
    Raw,
    Confirmed
}

public enum FairValueGapType
{
    Bullish,
    Bearish
}

public enum FocusedAnnotationKind
{
    Smt,
    Bos,
    Fvg,
    Ifvg,
    HalfBox,
    StopTakeProfit
}

public enum DetectionMode
{
    WickBreak,
    CloseConfirmation,
    Both
}

public enum AgentStatus
{
    Stopped,
    Running,
    Paused
}

public enum DataProviderMode
{
    YahooFinance,
    Mock
}

public enum DataConnectionStatus
{
    Connected,
    Updating,
    Delayed,
    Error
}
