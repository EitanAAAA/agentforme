namespace SMTAgent.Core.Models;

public sealed record MarketDataSnapshot(
    IReadOnlyList<Candle> EsCandles,
    IReadOnlyList<Candle> NqCandles,
    IReadOnlyList<SwingPoint> EsSwings,
    IReadOnlyList<SwingPoint> NqSwings,
    DataProviderMode DataProvider,
    DataConnectionStatus DataStatus,
    DateTime LastUpdated,
    string? StatusMessage);
