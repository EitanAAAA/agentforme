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
        var count = Math.Min(esCandles.Count, nqCandles.Count);
        var tolerance = settings.TickTolerance * settings.TickSize;
        var esFvgs = DetectFvgs(esCandles);
        var nqFvgs = DetectFvgs(nqCandles);

        for (var index = 0; index < count; index++)
        {
            if (esCandles[index].Time != nqCandles[index].Time)
            {
                continue;
            }

            DetectHighLow(signalsById, esCandles, nqCandles, esSwings, nqSwings, index, tolerance, settings);
            DetectFvgTap(signalsById, esCandles, nqCandles, esFvgs, nqFvgs, index, tolerance);
            DetectInvertedFvg(signalsById, esCandles, nqCandles, esFvgs, nqFvgs, index, tolerance);
        }

        return signalsById.Values
            .OrderBy(signal => signal.Time)
            .ThenBy(signal => signal.SetupType)
            .ThenBy(signal => signal.Type)
            .ThenBy(signal => signal.LeaderSymbol)
            .ToList();
    }

    private static void DetectHighLow(
        IDictionary<string, SmtSignal> signalsById,
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
            var esBreak = es.High > esHigh.Price + tolerance;
            var nqBreak = nq.High > nqHigh.Price + tolerance;
            if (esBreak ^ nqBreak)
            {
                var leader = esBreak ? es : nq;
                var failed = esBreak ? nq : es;
                AddHighLowSignal(
                    signalsById,
                    SmtSignalType.Bearish,
                    leader,
                    failed,
                    esHigh,
                    nqHigh,
                    es.High,
                    nq.High,
                    tolerance);
            }
        }

        var esLow = LatestConfirmedSwingBefore(esSwings, SwingPointType.Low, index, settings.SwingStrength, settings.SwingLookbackCandles);
        var nqLow = LatestConfirmedSwingBefore(nqSwings, SwingPointType.Low, index, settings.SwingStrength, settings.SwingLookbackCandles);
        if (esLow is not null && nqLow is not null)
        {
            var esBreak = es.Low < esLow.Price - tolerance;
            var nqBreak = nq.Low < nqLow.Price - tolerance;
            if (esBreak ^ nqBreak)
            {
                var leader = esBreak ? es : nq;
                var failed = esBreak ? nq : es;
                AddHighLowSignal(
                    signalsById,
                    SmtSignalType.Bullish,
                    leader,
                    failed,
                    esLow,
                    nqLow,
                    es.Low,
                    nq.Low,
                    tolerance);
            }
        }
    }

    private static void DetectFvgTap(
        IDictionary<string, SmtSignal> signalsById,
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles,
        IReadOnlyList<FairValueGap> esFvgs,
        IReadOnlyList<FairValueGap> nqFvgs,
        int index,
        decimal tolerance)
    {
        foreach (var fvgType in new[] { FairValueGapType.Bullish, FairValueGapType.Bearish })
        {
            var esFvg = LatestActiveFvgBefore(esFvgs, esCandles, fvgType, index, tolerance);
            var nqFvg = LatestActiveFvgBefore(nqFvgs, nqCandles, fvgType, index, tolerance);
            if (esFvg is null || nqFvg is null)
            {
                continue;
            }

            var es = esCandles[index];
            var nq = nqCandles[index];
            var esTapped = WickTouches(es, esFvg, tolerance);
            var nqTapped = WickTouches(nq, nqFvg, tolerance);
            if (!(esTapped ^ nqTapped))
            {
                continue;
            }

            var leader = esTapped ? es : nq;
            var failed = esTapped ? nq : es;
            var signalType = fvgType == FairValueGapType.Bullish ? SmtSignalType.Bullish : SmtSignalType.Bearish;
            AddFvgSignal(
                signalsById,
                SmtSetupType.Fvg,
                signalType,
                leader,
                failed,
                esFvg,
                nqFvg,
                CurrentFvgTouchValue(es, esFvg),
                CurrentFvgTouchValue(nq, nqFvg),
                $"{leader.Symbol} tapped, {failed.Symbol} failed",
                tolerance);
        }
    }

    private static void DetectInvertedFvg(
        IDictionary<string, SmtSignal> signalsById,
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles,
        IReadOnlyList<FairValueGap> esFvgs,
        IReadOnlyList<FairValueGap> nqFvgs,
        int index,
        decimal tolerance)
    {
        foreach (var fvgType in new[] { FairValueGapType.Bullish, FairValueGapType.Bearish })
        {
            var esFvg = LatestActiveFvgBefore(esFvgs, esCandles, fvgType, index, tolerance);
            var nqFvg = LatestActiveFvgBefore(nqFvgs, nqCandles, fvgType, index, tolerance);
            if (esFvg is null || nqFvg is null)
            {
                continue;
            }

            var es = esCandles[index];
            var nq = nqCandles[index];
            var esInverted = ClosesThroughFvg(es, esFvg, tolerance);
            var nqInverted = ClosesThroughFvg(nq, nqFvg, tolerance);
            if (!(esInverted ^ nqInverted))
            {
                continue;
            }

            var leader = esInverted ? es : nq;
            var failed = esInverted ? nq : es;
            var signalType = fvgType == FairValueGapType.Bullish ? SmtSignalType.Bearish : SmtSignalType.Bullish;
            AddFvgSignal(
                signalsById,
                SmtSetupType.InvertedFvg,
                signalType,
                leader,
                failed,
                esFvg,
                nqFvg,
                es.Close,
                nq.Close,
                $"{leader.Symbol} inverted, {failed.Symbol} held",
                tolerance);
        }
    }

    private static IReadOnlyList<FairValueGap> DetectFvgs(IReadOnlyList<Candle> candles)
    {
        var fvgs = new List<FairValueGap>();
        for (var index = 2; index < candles.Count; index++)
        {
            var first = candles[index - 2];
            var third = candles[index];

            if (first.High < third.Low)
            {
                fvgs.Add(new FairValueGap(
                    third.Symbol,
                    FairValueGapType.Bullish,
                    index,
                    first.Time,
                    third.Time,
                    first.High,
                    third.Low));
            }

            if (first.Low > third.High)
            {
                fvgs.Add(new FairValueGap(
                    third.Symbol,
                    FairValueGapType.Bearish,
                    index,
                    first.Time,
                    third.Time,
                    third.High,
                    first.Low));
            }
        }

        return fvgs;
    }

    private static void AddHighLowSignal(
        IDictionary<string, SmtSignal> signalsById,
        SmtSignalType type,
        Candle leader,
        Candle failed,
        SwingPoint esSwing,
        SwingPoint nqSwing,
        decimal esCurrentValue,
        decimal nqCurrentValue,
        decimal tolerance)
    {
        var leaderSwing = leader.Symbol == "ES" ? esSwing : nqSwing;
        var failedSwing = failed.Symbol == "ES" ? esSwing : nqSwing;
        var signalId = $"HL:{type}:{leader.Symbol}:{SwingId(leaderSwing)}:{SwingId(failedSwing)}";
        if (signalsById.ContainsKey(signalId))
        {
            return;
        }

        var leaderPrevious = leader.Symbol == "ES" ? esSwing.Price : nqSwing.Price;
        var failedPrevious = failed.Symbol == "ES" ? esSwing.Price : nqSwing.Price;
        var leaderCurrent = leader.Symbol == "ES" ? esCurrentValue : nqCurrentValue;
        var failedCurrent = failed.Symbol == "ES" ? esCurrentValue : nqCurrentValue;
        var side = type == SmtSignalType.Bearish ? "high" : "low";

        signalsById[signalId] = new SmtSignal(
            signalId,
            leader.Time,
            SmtSetupType.HighLow,
            type,
            SmtSignalStatus.Confirmed,
            leader.Symbol,
            failed.Symbol,
            $"SMT H/L: {leader.Symbol} broke {side}, {failed.Symbol} failed",
            "SMT H/L",
            $"{leader.Symbol} {leaderPrevious:0.00}->{leaderCurrent:0.00}; {failed.Symbol} {failedPrevious:0.00}->{failedCurrent:0.00}",
            DetectionMode.WickBreak,
            SwingId(leaderSwing),
            SwingId(failedSwing),
            leaderPrevious,
            leaderCurrent,
            failedPrevious,
            failedCurrent,
            tolerance,
            esSwing.Price,
            esCurrentValue,
            nqSwing.Price,
            nqCurrentValue,
            esSwing.Time,
            nqSwing.Time,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static void AddFvgSignal(
        IDictionary<string, SmtSignal> signalsById,
        SmtSetupType setupType,
        SmtSignalType signalType,
        Candle leader,
        Candle failed,
        FairValueGap esFvg,
        FairValueGap nqFvg,
        decimal esCurrentValue,
        decimal nqCurrentValue,
        string label,
        decimal tolerance)
    {
        var leaderFvg = leader.Symbol == "ES" ? esFvg : nqFvg;
        var failedFvg = failed.Symbol == "ES" ? esFvg : nqFvg;
        var prefix = setupType == SmtSetupType.Fvg ? "FVG" : "IFVG";
        var signalId = $"{prefix}:{leader.Symbol}:{leaderFvg.GapId}:{failedFvg.GapId}";
        if (signalsById.ContainsKey(signalId))
        {
            return;
        }

        var esMid = Mid(esFvg);
        var nqMid = Mid(nqFvg);
        var leaderPrevious = leader.Symbol == "ES" ? esMid : nqMid;
        var failedPrevious = failed.Symbol == "ES" ? esMid : nqMid;
        var leaderCurrent = leader.Symbol == "ES" ? esCurrentValue : nqCurrentValue;
        var failedCurrent = failed.Symbol == "ES" ? esCurrentValue : nqCurrentValue;
        var setupLabel = setupType == SmtSetupType.Fvg ? "SMT FVG" : "SMT IFVG";

        signalsById[signalId] = new SmtSignal(
            signalId,
            leader.Time,
            setupType,
            signalType,
            SmtSignalStatus.Confirmed,
            leader.Symbol,
            failed.Symbol,
            $"{setupLabel}: {label}",
            setupLabel,
            $"{leader.Symbol} zone {leaderFvg.Lower:0.00}-{leaderFvg.Upper:0.00}; {failed.Symbol} zone {failedFvg.Lower:0.00}-{failedFvg.Upper:0.00}",
            setupType == SmtSetupType.Fvg ? DetectionMode.WickBreak : DetectionMode.CloseConfirmation,
            leaderFvg.GapId,
            failedFvg.GapId,
            leaderPrevious,
            leaderCurrent,
            failedPrevious,
            failedCurrent,
            tolerance,
            esMid,
            esCurrentValue,
            nqMid,
            nqCurrentValue,
            esFvg.EndTime,
            nqFvg.EndTime,
            esFvg.Lower,
            esFvg.Upper,
            esFvg.StartTime,
            esFvg.EndTime,
            nqFvg.Lower,
            nqFvg.Upper,
            nqFvg.StartTime,
            nqFvg.EndTime);
    }

    private static FairValueGap? LatestActiveFvgBefore(
        IReadOnlyList<FairValueGap> fvgs,
        IReadOnlyList<Candle> candles,
        FairValueGapType type,
        int index,
        decimal tolerance)
    {
        return fvgs
            .Where(fvg => fvg.Type == type && fvg.CandleIndex < index && !WasInvalidatedBefore(fvg, candles, index, tolerance))
            .OrderByDescending(fvg => fvg.CandleIndex)
            .FirstOrDefault();
    }

    private static bool WasInvalidatedBefore(FairValueGap fvg, IReadOnlyList<Candle> candles, int index, decimal tolerance)
    {
        for (var i = fvg.CandleIndex + 1; i < index; i++)
        {
            if (ClosesThroughFvg(candles[i], fvg, tolerance))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WickTouches(Candle candle, FairValueGap fvg, decimal tolerance)
    {
        return candle.Low <= fvg.Upper + tolerance && candle.High >= fvg.Lower - tolerance;
    }

    private static bool ClosesThroughFvg(Candle candle, FairValueGap fvg, decimal tolerance)
    {
        return fvg.Type == FairValueGapType.Bullish
            ? candle.Close < fvg.Lower - tolerance
            : candle.Close > fvg.Upper + tolerance;
    }

    private static decimal CurrentFvgTouchValue(Candle candle, FairValueGap fvg)
    {
        return fvg.Type == FairValueGapType.Bullish
            ? Math.Min(candle.Low, fvg.Upper)
            : Math.Max(candle.High, fvg.Lower);
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

    private static decimal Mid(FairValueGap fvg)
    {
        return (fvg.Lower + fvg.Upper) / 2m;
    }
}
