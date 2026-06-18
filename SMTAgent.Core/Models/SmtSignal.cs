namespace SMTAgent.Core.Models;

public sealed record SmtSignal(
    DateTime Time,
    SmtSignalType Type,
    string LeaderSymbol,
    string FailedSymbol,
    string Reason,
    DetectionMode DetectionMode,
    decimal EsPreviousSwingValue,
    decimal EsCurrentValue,
    decimal NqPreviousSwingValue,
    decimal NqCurrentValue,
    DateTime EsPreviousSwingTime,
    DateTime NqPreviousSwingTime);
