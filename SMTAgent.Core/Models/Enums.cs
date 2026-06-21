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

public enum SmtSignalStatus
{
    Raw,
    Confirmed
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
