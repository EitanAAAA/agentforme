using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class SmtDetector
{
    public IReadOnlyList<SmtSignal> Detect(
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles,
        IReadOnlyList<SwingPoint> esSwings,
        IReadOnlyList<SwingPoint> nqSwings,
        DetectionMode detectionMode)
    {
        var signals = new List<SmtSignal>();
        var modes = ExpandModes(detectionMode);
        var count = Math.Min(esCandles.Count, nqCandles.Count);

        for (var index = 0; index < count; index++)
        {
            if (esCandles[index].Time != nqCandles[index].Time)
            {
                continue;
            }

            foreach (var mode in modes)
            {
                AddLeaderBreakSignals(signals, esCandles, nqCandles, esSwings, nqSwings, index, mode, esCandles, nqCandles);
                AddLeaderBreakSignals(signals, nqCandles, esCandles, nqSwings, esSwings, index, mode, esCandles, nqCandles);
            }
        }

        return signals;
    }

    private static IEnumerable<DetectionMode> ExpandModes(DetectionMode detectionMode)
    {
        if (detectionMode == DetectionMode.Both)
        {
            yield return DetectionMode.WickBreak;
            yield return DetectionMode.CloseConfirmation;
            yield break;
        }

        yield return detectionMode;
    }

    private static void AddLeaderBreakSignals(
        ICollection<SmtSignal> signals,
        IReadOnlyList<Candle> leaderCandles,
        IReadOnlyList<Candle> failedCandles,
        IReadOnlyList<SwingPoint> leaderSwings,
        IReadOnlyList<SwingPoint> failedSwings,
        int candleIndex,
        DetectionMode mode,
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles)
    {
        var leader = leaderCandles[candleIndex];
        var failed = failedCandles[candleIndex];
        var es = esCandles[candleIndex];
        var nq = nqCandles[candleIndex];

        var leaderHigh = LatestSwingBefore(leaderSwings, SwingPointType.High, candleIndex);
        var failedHigh = LatestSwingBefore(failedSwings, SwingPointType.High, candleIndex);
        if (leaderHigh is not null && failedHigh is not null &&
            BreaksHigh(leader, leaderHigh.Price, mode) &&
            !BreaksHigh(failed, failedHigh.Price, mode))
        {
            signals.Add(new SmtSignal(
                leader.Time,
                SmtSignalType.Bearish,
                leader.Symbol,
                failed.Symbol,
                $"{leader.Symbol} broke high, {failed.Symbol} failed.",
                mode,
                GetSwingValue("ES", leader, failed, leaderHigh, failedHigh),
                CurrentHighValue(es, mode),
                GetSwingValue("NQ", leader, failed, leaderHigh, failedHigh),
                CurrentHighValue(nq, mode),
                GetSwingTime("ES", leader, failed, leaderHigh, failedHigh),
                GetSwingTime("NQ", leader, failed, leaderHigh, failedHigh)));
        }

        var leaderLow = LatestSwingBefore(leaderSwings, SwingPointType.Low, candleIndex);
        var failedLow = LatestSwingBefore(failedSwings, SwingPointType.Low, candleIndex);
        if (leaderLow is not null && failedLow is not null &&
            BreaksLow(leader, leaderLow.Price, mode) &&
            !BreaksLow(failed, failedLow.Price, mode))
        {
            signals.Add(new SmtSignal(
                leader.Time,
                SmtSignalType.Bullish,
                leader.Symbol,
                failed.Symbol,
                $"{leader.Symbol} broke low, {failed.Symbol} failed.",
                mode,
                GetSwingValue("ES", leader, failed, leaderLow, failedLow),
                CurrentLowValue(es, mode),
                GetSwingValue("NQ", leader, failed, leaderLow, failedLow),
                CurrentLowValue(nq, mode),
                GetSwingTime("ES", leader, failed, leaderLow, failedLow),
                GetSwingTime("NQ", leader, failed, leaderLow, failedLow)));
        }
    }

    private static SwingPoint? LatestSwingBefore(IReadOnlyList<SwingPoint> swings, SwingPointType type, int candleIndex)
    {
        return swings
            .Where(swing => swing.Type == type && swing.CandleIndex < candleIndex)
            .OrderByDescending(swing => swing.CandleIndex)
            .FirstOrDefault();
    }

    private static bool BreaksHigh(Candle candle, decimal swingPrice, DetectionMode mode)
    {
        return mode switch
        {
            DetectionMode.WickBreak => candle.High > swingPrice,
            DetectionMode.CloseConfirmation => candle.Close > swingPrice,
            DetectionMode.Both => candle.High > swingPrice || candle.Close > swingPrice,
            _ => false
        };
    }

    private static bool BreaksLow(Candle candle, decimal swingPrice, DetectionMode mode)
    {
        return mode switch
        {
            DetectionMode.WickBreak => candle.Low < swingPrice,
            DetectionMode.CloseConfirmation => candle.Close < swingPrice,
            DetectionMode.Both => candle.Low < swingPrice || candle.Close < swingPrice,
            _ => false
        };
    }

    private static decimal CurrentHighValue(Candle candle, DetectionMode mode)
    {
        return mode == DetectionMode.CloseConfirmation ? candle.Close : candle.High;
    }

    private static decimal CurrentLowValue(Candle candle, DetectionMode mode)
    {
        return mode == DetectionMode.CloseConfirmation ? candle.Close : candle.Low;
    }

    private static decimal GetSwingValue(string symbol, Candle leader, Candle failed, SwingPoint leaderSwing, SwingPoint failedSwing)
    {
        return leader.Symbol == symbol ? leaderSwing.Price : failed.Symbol == symbol ? failedSwing.Price : 0m;
    }

    private static DateTime GetSwingTime(string symbol, Candle leader, Candle failed, SwingPoint leaderSwing, SwingPoint failedSwing)
    {
        return leader.Symbol == symbol ? leaderSwing.Time : failed.Symbol == symbol ? failedSwing.Time : DateTime.MinValue;
    }
}
