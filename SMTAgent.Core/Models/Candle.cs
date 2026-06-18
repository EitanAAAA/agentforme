namespace SMTAgent.Core.Models;

public sealed record Candle(
    string Symbol,
    DateTime Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
