namespace SMTAgent.Core.Models;

public sealed record MarketDataSet(IReadOnlyList<Candle> EsCandles, IReadOnlyList<Candle> NqCandles);
