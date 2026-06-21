using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class SmtDetector
{
    public IReadOnlyList<SmtSignal> Detect(
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles,
        IReadOnlyList<SwingPoint> esSwings,
        IReadOnlyList<SwingPoint> nqSwings,
        AgentSettings settings)
    {
        var signalsById = new Dictionary<string, SmtSignal>();
        var consumedLeaderSwings = new HashSet<string>();
        var lastSignalByZone = new Dictionary<string, int>();
        var count = Math.Min(esCandles.Count, nqCandles.Count);
        var tolerance = settings.TickTolerance * settings.TickSize;

        for (var index = 0; index < count; index++)
        {
            if (esCandles[index].Time != nqCandles[index].Time)
            {
                continue;
            }

            DetectAtIndex(
                signalsById,
                consumedLeaderSwings,
                lastSignalByZone,
                esCandles,
                nqCandles,
                esSwings,
                nqSwings,
                index,
                tolerance,
                settings);
        }

        return signalsById.Values
            .OrderBy(signal => signal.Time)
            .ThenBy(signal => signal.Type)
            .ThenBy(signal => signal.LeaderSymbol)
            .ToList();
    }

    private static void DetectAtIndex(
        IDictionary<string, SmtSignal> signalsById,
        ISet<string> consumedLeaderSwings,
        IDictionary<string, int> lastSignalByZone,
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles,
        IReadOnlyList<SwingPoint> esSwings,
        IReadOnlyList<SwingPoint> nqSwings,
        int index,
        decimal tolerance,
        AgentSettings settings)
    {
        var es = esCandles[index];
        var nq = nqCandles[index];

        var esHigh = LatestConfirmedSwingBefore(esSwings, SwingPointType.High, index, settings.SwingStrength, settings.SwingLookbackCandles);
        var nqHigh = LatestConfirmedSwingBefore(nqSwings, SwingPointType.High, index, settings.SwingStrength, settings.SwingLookbackCandles);
        if (esHigh is not null && nqHigh is not null)
        {
            DetectBearish(signalsById, consumedLeaderSwings, lastSignalByZone, es, nq, esHigh, nqHigh, index, tolerance, settings);
        }

        var esLow = LatestConfirmedSwingBefore(esSwings, SwingPointType.Low, index, settings.SwingStrength, settings.SwingLookbackCandles);
        var nqLow = LatestConfirmedSwingBefore(nqSwings, SwingPointType.Low, index, settings.SwingStrength, settings.SwingLookbackCandles);
        if (esLow is not null && nqLow is not null)
        {
            DetectBullish(signalsById, consumedLeaderSwings, lastSignalByZone, es, nq, esLow, nqLow, index, tolerance, settings);
        }
    }

    private static void DetectBearish(
        IDictionary<string, SmtSignal> signalsById,
        ISet<string> consumedLeaderSwings,
        IDictionary<string, int> lastSignalByZone,
        Candle es,
        Candle nq,
        SwingPoint esHigh,
        SwingPoint nqHigh,
        int index,
        decimal tolerance,
        AgentSettings settings)
    {
        var esBreak = es.High > esHigh.Price + tolerance;
        var nqBreak = nq.High > nqHigh.Price + tolerance;
        if (esBreak == nqBreak)
        {
            return;
        }

        var leader = esBreak ? es : nq;
        var failed = esBreak ? nq : es;
        var leaderSwing = esBreak ? esHigh : nqHigh;
        var failedSwing = esBreak ? nqHigh : esHigh;
        var leaderConfirmed = leader.High > leaderSwing.Price + tolerance && leader.Close < leaderSwing.Price;
        if (!TryGetStatus(settings.DetectionMode, leaderConfirmed, out var status))
        {
            return;
        }

        AddSignal(
            signalsById,
            consumedLeaderSwings,
            lastSignalByZone,
            SmtSignalType.Bearish,
            status,
            leader,
            failed,
            leaderSwing,
            failedSwing,
            esHigh,
            nqHigh,
            es.High,
            nq.High,
            tolerance,
            index,
            settings);
    }

    private static void DetectBullish(
        IDictionary<string, SmtSignal> signalsById,
        ISet<string> consumedLeaderSwings,
        IDictionary<string, int> lastSignalByZone,
        Candle es,
        Candle nq,
        SwingPoint esLow,
        SwingPoint nqLow,
        int index,
        decimal tolerance,
        AgentSettings settings)
    {
        var esBreak = es.Low < esLow.Price - tolerance;
        var nqBreak = nq.Low < nqLow.Price - tolerance;
        if (esBreak == nqBreak)
        {
            return;
        }

        var leader = esBreak ? es : nq;
        var failed = esBreak ? nq : es;
        var leaderSwing = esBreak ? esLow : nqLow;
        var failedSwing = esBreak ? nqLow : esLow;
        var leaderConfirmed = leader.Low < leaderSwing.Price - tolerance && leader.Close > leaderSwing.Price;
        if (!TryGetStatus(settings.DetectionMode, leaderConfirmed, out var status))
        {
            return;
        }

        AddSignal(
            signalsById,
            consumedLeaderSwings,
            lastSignalByZone,
            SmtSignalType.Bullish,
            status,
            leader,
            failed,
            leaderSwing,
            failedSwing,
            esLow,
            nqLow,
            es.Low,
            nq.Low,
            tolerance,
            index,
            settings);
    }

    private static void AddSignal(
        IDictionary<string, SmtSignal> signalsById,
        ISet<string> consumedLeaderSwings,
        IDictionary<string, int> lastSignalByZone,
        SmtSignalType type,
        SmtSignalStatus status,
        Candle leader,
        Candle failed,
        SwingPoint leaderSwing,
        SwingPoint failedSwing,
        SwingPoint esReferenceSwing,
        SwingPoint nqReferenceSwing,
        decimal esCurrentValue,
        decimal nqCurrentValue,
        decimal tolerance,
        int index,
        AgentSettings settings)
    {
        var leaderSwingId = SwingId(leaderSwing);
        var failedSwingId = SwingId(failedSwing);
        var consumedKey = $"{type}:{leader.Symbol}:{leaderSwingId}";
        var signalId = $"{type}:{leader.Symbol}:{failed.Symbol}:{leaderSwingId}:{failedSwingId}:{leader.Time:O}";
        if (consumedLeaderSwings.Contains(consumedKey) && !signalsById.ContainsKey(signalId))
        {
            return;
        }

        var zoneKey = $"{type}:{leader.Symbol}:{leaderSwingId}:{failedSwingId}";
        if (lastSignalByZone.TryGetValue(zoneKey, out var lastSignalIndex) &&
            index - lastSignalIndex < settings.AlertCooldownCandles &&
            !signalsById.ContainsKey(signalId))
        {
            return;
        }

        var mode = status == SmtSignalStatus.Confirmed ? DetectionMode.CloseConfirmation : DetectionMode.WickBreak;
        var leaderCurrentValue = leader.Symbol == "ES" ? esCurrentValue : nqCurrentValue;
        var failedCurrentValue = failed.Symbol == "ES" ? esCurrentValue : nqCurrentValue;
        var calculationSummary = BuildCalculationSummary(
            type,
            leader.Symbol,
            leaderSwing.Price,
            leaderCurrentValue,
            failed.Symbol,
            failedSwing.Price,
            failedCurrentValue,
            tolerance,
            status);

        var signal = new SmtSignal(
            signalId,
            leader.Time,
            type,
            status,
            leader.Symbol,
            failed.Symbol,
            BuildReason(type, leader.Symbol, failed.Symbol, status),
            status == SmtSignalStatus.Raw ? "Waiting for confirmation" : "Confirmed SMT",
            calculationSummary,
            mode,
            leaderSwingId,
            failedSwingId,
            leaderSwing.Price,
            leaderCurrentValue,
            failedSwing.Price,
            failedCurrentValue,
            tolerance,
            esReferenceSwing.Price,
            esCurrentValue,
            nqReferenceSwing.Price,
            nqCurrentValue,
            esReferenceSwing.Time,
            nqReferenceSwing.Time);

        if (signalsById.TryGetValue(signalId, out var existing) &&
            existing.Status == SmtSignalStatus.Confirmed)
        {
            return;
        }

        signalsById[signalId] = signal;
        consumedLeaderSwings.Add(consumedKey);
        lastSignalByZone[zoneKey] = index;
    }

    private static bool TryGetStatus(DetectionMode mode, bool leaderConfirmed, out SmtSignalStatus status)
    {
        status = SmtSignalStatus.Raw;
        switch (mode)
        {
            case DetectionMode.WickBreak:
                return true;
            case DetectionMode.CloseConfirmation when leaderConfirmed:
                status = SmtSignalStatus.Confirmed;
                return true;
            case DetectionMode.Both:
                status = leaderConfirmed ? SmtSignalStatus.Confirmed : SmtSignalStatus.Raw;
                return true;
            default:
                return false;
        }
    }

    private static SwingPoint? LatestConfirmedSwingBefore(
        IReadOnlyList<SwingPoint> swings,
        SwingPointType type,
        int candleIndex,
        int strength,
        int lookbackCandles)
    {
        var earliestIndex = Math.Max(0, candleIndex - lookbackCandles);
        return swings
            .Where(swing =>
                swing.Type == type &&
                swing.CandleIndex >= earliestIndex &&
                swing.CandleIndex + strength < candleIndex)
            .OrderByDescending(swing => swing.CandleIndex)
            .FirstOrDefault();
    }

    private static string SwingId(SwingPoint swing)
    {
        return $"{swing.Symbol}:{swing.Type}:{swing.Time:O}:{swing.Price:0.########}:{swing.CandleIndex}";
    }

    private static string BuildReason(SmtSignalType type, string leaderSymbol, string failedSymbol, SmtSignalStatus status)
    {
        var statusText = status == SmtSignalStatus.Confirmed ? "Confirmed" : "Raw";
        if (type == SmtSignalType.Bearish)
        {
            return status == SmtSignalStatus.Confirmed
                ? $"{statusText}: {leaderSymbol} swept above the previous swing high and closed back below it; {failedSymbol} failed to break high."
                : $"{statusText}: {leaderSymbol} broke high by wick; {failedSymbol} failed to break high.";
        }

        return status == SmtSignalStatus.Confirmed
            ? $"{statusText}: {leaderSymbol} swept below the previous swing low and closed back above it; {failedSymbol} failed to break low."
            : $"{statusText}: {leaderSymbol} broke low by wick; {failedSymbol} failed to break low.";
    }

    private static string BuildCalculationSummary(
        SmtSignalType type,
        string leaderSymbol,
        decimal leaderPrevious,
        decimal leaderCurrent,
        string failedSymbol,
        decimal failedPrevious,
        decimal failedCurrent,
        decimal tolerance,
        SmtSignalStatus status)
    {
        var side = type == SmtSignalType.Bearish ? "high" : "low";
        return $"{status} {side}: {leaderSymbol} prev {leaderPrevious:0.00}, current {leaderCurrent:0.00}; " +
            $"{failedSymbol} prev {failedPrevious:0.00}, current {failedCurrent:0.00}; tol {tolerance:0.##}";
    }
}
