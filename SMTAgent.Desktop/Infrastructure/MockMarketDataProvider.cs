using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class MockMarketDataProvider
{
    public MarketDataSet Generate(string timeframe)
    {
        var interval = timeframe switch
        {
            "30s" => TimeSpan.FromSeconds(30),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromMinutes(1)
        };

        var start = DateTime.Today.AddHours(9).AddMinutes(30);

        var es = new[]
        {
            100m, 101m, 102m, 103m, 104m, 108m, 105m, 103m, 101m, 103m,
            112m, 108m, 104m, 102m, 106m, 107m, 105m, 103m, 105m, 104m,
            104m, 103m, 101m, 103m, 104m, 103m, 92m, 96m, 99m, 101m,
            100m, 98m, 96m, 95m, 96m, 97m, 99m, 100m, 101m, 101m
        };

        var nq = new[]
        {
            200m, 201m, 203m, 204m, 205m, 210m, 207m, 205m, 202m, 204m,
            208m, 206m, 204m, 203m, 205m, 207m, 206m, 205m, 216m, 211m,
            207m, 205m, 197m, 200m, 204m, 202m, 200m, 203m, 203m, 204m,
            204m, 201m, 201m, 200m, 189m, 193m, 198m, 202m, 204m, 204m
        };

        return new MarketDataSet(
            BuildCandles("ES", es, start, interval, 5000),
            BuildCandles("NQ", nq, start, interval, 7200));
    }

    private static IReadOnlyList<Candle> BuildCandles(
        string symbol,
        IReadOnlyList<decimal> closes,
        DateTime start,
        TimeSpan interval,
        long baseVolume)
    {
        var candles = new List<Candle>(closes.Count);
        var previousClose = closes[0] - 0.6m;

        for (var index = 0; index < closes.Count; index++)
        {
            var close = closes[index];
            var open = previousClose + ((close - previousClose) * 0.35m);
            var high = Math.Max(open, close) + 0.8m;
            var low = Math.Min(open, close) - 0.8m;

            candles.Add(new Candle(
                symbol,
                start.AddTicks(interval.Ticks * index),
                open,
                high,
                low,
                close,
                baseVolume + (index * 137)));

            previousClose = close;
        }

        return candles;
    }
}
