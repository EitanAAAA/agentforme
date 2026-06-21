using System.Net;
using System.Text.Json;
using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class YahooFinanceMarketDataProvider
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static YahooFinanceMarketDataProvider()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SMTAgent/1.0 delayed analytics dashboard");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<MarketDataSet> FetchAsync(CancellationToken cancellationToken)
    {
        var esTask = FetchSymbolAsync("ES", "ES=F", cancellationToken);
        var nqTask = FetchSymbolAsync("NQ", "NQ=F", cancellationToken);
        await Task.WhenAll(esTask, nqTask);

        return SyncByTimestamp(await esTask, await nqTask);
    }

    private static async Task<IReadOnlyList<Candle>> FetchSymbolAsync(
        string displaySymbol,
        string yahooSymbol,
        CancellationToken cancellationToken)
    {
        var encodedSymbol = WebUtility.UrlEncode(yahooSymbol);
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{encodedSymbol}?range=5d&interval=15m";
        using var response = await GetWithRetryAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var chart = document.RootElement.GetProperty("chart");
        var error = chart.GetProperty("error");
        if (error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"Yahoo Finance returned an error for {yahooSymbol}: {error}");
        }

        var result = chart.GetProperty("result")[0];
        if (!result.TryGetProperty("timestamp", out var timestamps))
        {
            return [];
        }

        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");

        var candles = new List<Candle>(timestamps.GetArrayLength());
        for (var index = 0; index < timestamps.GetArrayLength(); index++)
        {
            if (!TryGetDecimal(opens, index, out var open) ||
                !TryGetDecimal(highs, index, out var high) ||
                !TryGetDecimal(lows, index, out var low) ||
                !TryGetDecimal(closes, index, out var close))
            {
                continue;
            }

            var volume = TryGetLong(volumes, index, out var parsedVolume) ? parsedVolume : 0;
            var timestamp = timestamps[index].GetInt64();
            var time = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;

            candles.Add(new Candle(displaySymbol, time, open, high, low, close, volume));
        }

        return candles
            .GroupBy(candle => candle.Time)
            .Select(group => group.Last())
            .OrderBy(candle => candle.Time)
            .ToList();
    }

    private static async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        var response = await HttpClient.GetAsync(url, cancellationToken);
        if ((int)response.StatusCode != 429)
        {
            return response;
        }

        response.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return await HttpClient.GetAsync(url, cancellationToken);
    }

    private static MarketDataSet SyncByTimestamp(IReadOnlyList<Candle> esCandles, IReadOnlyList<Candle> nqCandles)
    {
        var esByTime = esCandles.ToDictionary(candle => candle.Time);
        var nqByTime = nqCandles.ToDictionary(candle => candle.Time);
        var sharedTimes = esByTime.Keys.Intersect(nqByTime.Keys).OrderBy(time => time).ToList();

        return new MarketDataSet(
            sharedTimes.Select(time => esByTime[time]).ToList(),
            sharedTimes.Select(time => nqByTime[time]).ToList());
    }

    private static bool TryGetDecimal(JsonElement array, int index, out decimal value)
    {
        value = 0m;
        if (index >= array.GetArrayLength() || array[index].ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (!array[index].TryGetDouble(out var parsed) || double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            return false;
        }

        value = Convert.ToDecimal(parsed);
        return true;
    }

    private static bool TryGetLong(JsonElement array, int index, out long value)
    {
        value = 0;
        return index < array.GetArrayLength() &&
            array[index].ValueKind != JsonValueKind.Null &&
            array[index].TryGetInt64(out value);
    }
}

