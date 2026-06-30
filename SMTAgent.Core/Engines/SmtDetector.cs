using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class SmtDetector
{
    private const int HighLowSwingStrength = 1;

    public IReadOnlyList<SmtSignal> Detect(
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles,
        IReadOnlyList<SwingPoint> esSwings,
        IReadOnlyList<SwingPoint> nqSwings,
        AgentSettings settings)
    {
        var signalsById = new Dictionary<string, SmtSignal>();
        var tolerance = settings.TickTolerance * settings.TickSize;
        var esFvgs = DetectFvgs(esCandles);
        var nqFvgs = DetectFvgs(nqCandles);
        var esHighLowSwings = DetectHighLowSwings(esCandles);
        var nqHighLowSwings = DetectHighLowSwings(nqCandles);
        var esIndexByTime = BuildIndexByTime(esCandles);
        var nqIndexByTime = BuildIndexByTime(nqCandles);
        var synchronizedTimes = esIndexByTime.Keys
            .Intersect(nqIndexByTime.Keys)
            .OrderBy(time => time);

        foreach (var time in synchronizedTimes)
        {
            var esIndex = esIndexByTime[time];
            var nqIndex = nqIndexByTime[time];
            DetectHighLow(signalsById, esCandles, nqCandles, esHighLowSwings, nqHighLowSwings, esIndex, nqIndex, tolerance, settings);
            DetectFvgTap(signalsById, esCandles, nqCandles, esFvgs, nqFvgs, esIndex, nqIndex, tolerance);
            DetectInvertedFvg(signalsById, esCandles, nqCandles, esFvgs, nqFvgs, esIndex, nqIndex, tolerance);
        }

        var highLowSignals = signalsById.Values
            .Select(signal => signal.SetupType == SmtSetupType.HighLow
                ? UpdateActiveHighLowSignal(signal, esCandles, nqCandles, tolerance)
                : signal)
            .Where(signal => signal is not null)
            .Cast<SmtSignal>();

        return highLowSignals
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
        int esIndex,
        int nqIndex,
        decimal tolerance,
        AgentSettings settings)
    {
        var es = esCandles[esIndex];
        var nq = nqCandles[nqIndex];
        if (es.Time != nq.Time)
        {
            return;
        }

        if (settings.ShowBearishSmt)
        {
            foreach (var (esHigh, nqHigh) in PairedConfirmedSwingsBefore(esSwings, nqSwings, SwingPointType.High, esIndex, nqIndex, settings.SwingLookbackCandles))
            {
                var esBreak = IsFreshBreak(esCandles, esHigh, esIndex, SmtSignalType.Bearish, tolerance);
                var nqBreak = IsFreshBreak(nqCandles, nqHigh, nqIndex, SmtSignalType.Bearish, tolerance);
                var esHeldHigh = !HasBrokenReference(esCandles, esHigh, esIndex, SmtSignalType.Bearish, tolerance);
                var nqHeldHigh = !HasBrokenReference(nqCandles, nqHigh, nqIndex, SmtSignalType.Bearish, tolerance);
                if ((esBreak && nqHeldHigh) || (nqBreak && esHeldHigh))
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
                        tolerance,
                        settings.DetectionMode);
                }
            }
        }

        if (settings.ShowBullishSmt)
        {
            foreach (var (esLow, nqLow) in PairedConfirmedSwingsBefore(esSwings, nqSwings, SwingPointType.Low, esIndex, nqIndex, settings.SwingLookbackCandles))
            {
                var esBreak = IsFreshBreak(esCandles, esLow, esIndex, SmtSignalType.Bullish, tolerance);
                var nqBreak = IsFreshBreak(nqCandles, nqLow, nqIndex, SmtSignalType.Bullish, tolerance);
                var esHeldLow = !HasBrokenReference(esCandles, esLow, esIndex, SmtSignalType.Bullish, tolerance);
                var nqHeldLow = !HasBrokenReference(nqCandles, nqLow, nqIndex, SmtSignalType.Bullish, tolerance);
                if ((esBreak && nqHeldLow) || (nqBreak && esHeldLow))
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
                        tolerance,
                        settings.DetectionMode);
                }
            }
        }
    }

    private static void DetectFvgTap(
        IDictionary<string, SmtSignal> signalsById,
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles,
        IReadOnlyList<FairValueGap> esFvgs,
        IReadOnlyList<FairValueGap> nqFvgs,
        int esIndex,
        int nqIndex,
        decimal tolerance)
    {
        foreach (var fvgType in new[] { FairValueGapType.Bullish, FairValueGapType.Bearish })
        {
            var esFvg = LatestActiveFvgBefore(esFvgs, esCandles, fvgType, esIndex, tolerance);
            var nqFvg = LatestActiveFvgBefore(nqFvgs, nqCandles, fvgType, nqIndex, tolerance);
            if (esFvg is null || nqFvg is null)
            {
                continue;
            }

            var es = esCandles[esIndex];
            var nq = nqCandles[nqIndex];
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
        int esIndex,
        int nqIndex,
        decimal tolerance)
    {
        foreach (var fvgType in new[] { FairValueGapType.Bullish, FairValueGapType.Bearish })
        {
            var esFvg = LatestActiveFvgBefore(esFvgs, esCandles, fvgType, esIndex, tolerance);
            var nqFvg = LatestActiveFvgBefore(nqFvgs, nqCandles, fvgType, nqIndex, tolerance);
            if (esFvg is null || nqFvg is null)
            {
                continue;
            }

            var es = esCandles[esIndex];
            var nq = nqCandles[nqIndex];
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
        decimal tolerance,
        DetectionMode detectionMode)
    {
        if (leader.Time != failed.Time)
        {
            return;
        }

        if (!SameReferenceTime(esSwing, nqSwing))
        {
            return;
        }

        var leaderSwing = leader.Symbol == "ES" ? esSwing : nqSwing;
        var failedSwing = failed.Symbol == "ES" ? esSwing : nqSwing;
        var referenceKey = $"HL:{type}:{leader.Symbol}:{failed.Symbol}:{SwingId(leaderSwing)}:{SwingId(failedSwing)}";

        var leaderPrevious = leader.Symbol == "ES" ? esSwing.Price : nqSwing.Price;
        var failedPrevious = failed.Symbol == "ES" ? esSwing.Price : nqSwing.Price;
        var leaderCurrent = leader.Symbol == "ES" ? esCurrentValue : nqCurrentValue;
        var failedCurrent = failed.Symbol == "ES" ? esCurrentValue : nqCurrentValue;
        var side = type == SmtSignalType.Bearish ? "high" : "low";
        var closeConfirmed = type == SmtSignalType.Bullish
            ? leader.Low < leaderPrevious - tolerance && leader.Close > leaderPrevious + tolerance
            : leader.High > leaderPrevious + tolerance && leader.Close < leaderPrevious - tolerance;
        var status = detectionMode switch
        {
            DetectionMode.CloseConfirmation when closeConfirmed => SmtSignalStatus.Confirmed,
            DetectionMode.Both when closeConfirmed => SmtSignalStatus.Confirmed,
            DetectionMode.Both => SmtSignalStatus.Raw,
            DetectionMode.WickBreak => SmtSignalStatus.Confirmed,
            _ => SmtSignalStatus.Raw
        };
        if (detectionMode == DetectionMode.CloseConfirmation && !closeConfirmed)
        {
            return;
        }

        var signalId = $"{referenceKey}:{leader.Time:O}";
        if (signalsById.TryGetValue(signalId, out var existing) && existing.Status == SmtSignalStatus.Confirmed)
        {
            return;
        }
        signalsById[signalId] = new SmtSignal(
            signalId,
            leader.Time,
            SmtSetupType.HighLow,
            type,
            status,
            leader.Symbol,
            failed.Symbol,
            type == SmtSignalType.Bullish
                ? $"{leader.Symbol} broke low, {failed.Symbol} held higher low"
                : $"{leader.Symbol} broke high, {failed.Symbol} held lower high",
            type == SmtSignalType.Bullish ? "Bullish SMT" : "Bearish SMT",
            $"{leader.Symbol} {leaderPrevious:0.00}->{leaderCurrent:0.00}; {failed.Symbol} {failedPrevious:0.00}->{failedCurrent:0.00}",
            detectionMode == DetectionMode.Both && status == SmtSignalStatus.Raw ? DetectionMode.WickBreak : detectionMode,
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
            leader.Symbol == "ES" ? leader.Time : failed.Time,
            leader.Symbol == "NQ" ? leader.Time : failed.Time,
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
        if (leader.Time != failed.Time)
        {
            return;
        }

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
            CurrentTimeForSymbol(leader, failed, "ES"),
            CurrentTimeForSymbol(leader, failed, "NQ"),
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

    private static IReadOnlyList<SwingPoint> DetectHighLowSwings(IReadOnlyList<Candle> candles)
    {
        var swings = new List<SwingPoint>();
        if (candles.Count < 3)
        {
            return swings;
        }

        for (var index = 1; index < candles.Count - 1; index++)
        {
            var candle = candles[index];
            if (candle.High > candles[index - 1].High && candle.High > candles[index + 1].High)
            {
                swings.Add(new SwingPoint(candle.Symbol, candle.Time, candle.High, SwingPointType.High, index));
            }

            if (candle.Low < candles[index - 1].Low && candle.Low < candles[index + 1].Low)
            {
                swings.Add(new SwingPoint(candle.Symbol, candle.Time, candle.Low, SwingPointType.Low, index));
            }
        }

        return swings;
    }

    private static IEnumerable<(SwingPoint EsSwing, SwingPoint NqSwing)> PairedConfirmedSwingsBefore(
        IReadOnlyList<SwingPoint> esSwings,
        IReadOnlyList<SwingPoint> nqSwings,
        SwingPointType type,
        int esIndex,
        int nqIndex,
        int lookbackCandles)
    {
        var earliestEsIndex = Math.Max(0, esIndex - lookbackCandles);
        var earliestNqIndex = Math.Max(0, nqIndex - lookbackCandles);
        var nqByTime = nqSwings
            .Where(swing =>
                swing.Type == type &&
                swing.CandleIndex >= earliestNqIndex &&
                swing.CandleIndex + HighLowSwingStrength <= nqIndex)
            .GroupBy(swing => swing.Time)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(swing => swing.CandleIndex).First());

        return esSwings
            .Where(swing =>
                swing.Type == type &&
                swing.CandleIndex >= earliestEsIndex &&
                swing.CandleIndex + HighLowSwingStrength <= esIndex &&
                nqByTime.ContainsKey(swing.Time))
            .OrderByDescending(swing => swing.CandleIndex)
            .Select(swing => (swing, nqByTime[swing.Time]));
    }

    private static bool IsFreshBreak(
        IReadOnlyList<Candle> candles,
        SwingPoint reference,
        int currentIndex,
        SmtSignalType type,
        decimal tolerance)
    {
        return BreaksReference(candles[currentIndex], reference.Price, type, tolerance) &&
            !HasBrokenReference(candles, reference, currentIndex - 1, type, tolerance);
    }

    private static bool HasBrokenReference(
        IReadOnlyList<Candle> candles,
        SwingPoint reference,
        int throughIndex,
        SmtSignalType type,
        decimal tolerance)
    {
        var startIndex = Math.Max(reference.CandleIndex + 1, 0);
        var endIndex = Math.Min(throughIndex, candles.Count - 1);
        for (var index = startIndex; index <= endIndex; index++)
        {
            if (BreaksReference(candles[index], reference.Price, type, tolerance))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BreaksReference(Candle candle, decimal reference, SmtSignalType type, decimal tolerance)
    {
        return type == SmtSignalType.Bearish
            ? candle.High > reference + tolerance
            : candle.Low < reference - tolerance;
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

    private static Dictionary<DateTime, int> BuildIndexByTime(IReadOnlyList<Candle> candles)
    {
        var indexByTime = new Dictionary<DateTime, int>();
        for (var index = 0; index < candles.Count; index++)
        {
            indexByTime[candles[index].Time] = index;
        }

        return indexByTime;
    }

    private static SmtSignal? UpdateActiveHighLowSignal(
        SmtSignal signal,
        IReadOnlyList<Candle> esCandles,
        IReadOnlyList<Candle> nqCandles,
        decimal tolerance)
    {
        var leaderCandles = signal.LeaderSymbol == "ES" ? esCandles : nqCandles;
        var failedIndexByTime = BuildIndexByTime(signal.FailedSymbol == "ES" ? esCandles : nqCandles);
        var bestTime = signal.LeaderSymbol == "ES" ? signal.EsCurrentTime : signal.NqCurrentTime;
        var bestLeaderValue = signal.LeaderCurrentValue;
        var bestFailedValue = signal.FailedCurrentValue;

        foreach (var leader in leaderCandles.Where(candle => candle.Time >= signal.Time).OrderBy(candle => candle.Time))
        {
            if (!failedIndexByTime.TryGetValue(leader.Time, out var failedIndex))
            {
                continue;
            }

            var failed = (signal.FailedSymbol == "ES" ? esCandles : nqCandles)[failedIndex];
            if (FailedSideInvalidated(signal.Type, failed, signal.FailedPreviousSwingValue, tolerance))
            {
                var canceledLeaderValue = ExtremeValue(leader, signal.Type);
                var canceledFailedValue = ExtremeValue(failed, signal.Type);
                var canceledEsValue = signal.LeaderSymbol == "ES" ? canceledLeaderValue : canceledFailedValue;
                var canceledNqValue = signal.LeaderSymbol == "NQ" ? canceledLeaderValue : canceledFailedValue;
                var side = signal.Type == SmtSignalType.Bearish ? "high" : "low";
                return signal with
                {
                    Status = SmtSignalStatus.Canceled,
                    LeaderCurrentValue = canceledLeaderValue,
                    FailedCurrentValue = canceledFailedValue,
                    EsCurrentValue = canceledEsValue,
                    NqCurrentValue = canceledNqValue,
                    EsCurrentTime = leader.Time,
                    NqCurrentTime = leader.Time,
                    CalculationSummary = $"{signal.LeaderSymbol} {signal.LeaderPreviousSwingValue:0.00}->{canceledLeaderValue:0.00}; {signal.FailedSymbol} {signal.FailedPreviousSwingValue:0.00}->{canceledFailedValue:0.00}",
                    Reason = signal.Type == SmtSignalType.Bullish
                        ? $"{signal.LeaderSymbol} broke low, {signal.FailedSymbol} later broke low"
                        : $"{signal.LeaderSymbol} broke high, {signal.FailedSymbol} later broke high",
                    WorkflowState = $"SMT canceled: {signal.FailedSymbol} reached {side}"
                };
            }

            var leaderValue = ExtremeValue(leader, signal.Type);
            if (!IsMoreExtreme(signal.Type, leaderValue, bestLeaderValue))
            {
                continue;
            }

            bestTime = leader.Time;
            bestLeaderValue = leaderValue;
            bestFailedValue = ExtremeValue(failed, signal.Type);
        }

        var esCurrentValue = signal.LeaderSymbol == "ES" ? bestLeaderValue : bestFailedValue;
        var nqCurrentValue = signal.LeaderSymbol == "NQ" ? bestLeaderValue : bestFailedValue;
        return signal with
        {
            LeaderCurrentValue = bestLeaderValue,
            FailedCurrentValue = bestFailedValue,
            EsCurrentValue = esCurrentValue,
            NqCurrentValue = nqCurrentValue,
            EsCurrentTime = bestTime,
            NqCurrentTime = bestTime,
            CalculationSummary = $"{signal.LeaderSymbol} {signal.LeaderPreviousSwingValue:0.00}->{bestLeaderValue:0.00}; {signal.FailedSymbol} {signal.FailedPreviousSwingValue:0.00}->{bestFailedValue:0.00}",
            Reason = signal.Type == SmtSignalType.Bullish
                ? $"{signal.LeaderSymbol} broke low, {signal.FailedSymbol} held higher low"
                : $"{signal.LeaderSymbol} broke high, {signal.FailedSymbol} held lower high",
            WorkflowState = signal.Type == SmtSignalType.Bullish ? "Bullish SMT" : "Bearish SMT"
        };
    }

    private static bool FailedSideInvalidated(SmtSignalType type, Candle failed, decimal failedReference, decimal tolerance)
    {
        return type == SmtSignalType.Bullish
            ? failed.Low < failedReference - tolerance
            : failed.High > failedReference + tolerance;
    }

    private static decimal ExtremeValue(Candle candle, SmtSignalType type)
    {
        return type == SmtSignalType.Bullish ? candle.Low : candle.High;
    }

    private static bool IsMoreExtreme(SmtSignalType type, decimal candidate, decimal current)
    {
        return type == SmtSignalType.Bullish ? candidate < current : candidate > current;
    }

    private static DateTime CurrentTimeForSymbol(Candle leader, Candle failed, string symbol)
    {
        return leader.Symbol == symbol ? leader.Time : failed.Time;
    }

    private static string SwingId(SwingPoint swing)
    {
        return $"{swing.Symbol}:{swing.Type}:{swing.Time:O}:{swing.Price:0.########}:{swing.CandleIndex}";
    }

    private static bool SameReferenceTime(SwingPoint esSwing, SwingPoint nqSwing)
    {
        return esSwing.Time == nqSwing.Time;
    }

    private static decimal Mid(FairValueGap fvg)
    {
        return (fvg.Lower + fvg.Upper) / 2m;
    }
}
