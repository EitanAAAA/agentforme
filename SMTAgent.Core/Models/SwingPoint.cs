namespace SMTAgent.Core.Models;

public sealed record SwingPoint(
    string Symbol,
    DateTime Time,
    decimal Price,
    SwingPointType Type,
    int CandleIndex);
