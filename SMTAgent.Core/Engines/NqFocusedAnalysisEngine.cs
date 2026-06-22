using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class NqFocusedAnalysisEngine
{
    private static readonly TimeSpan PreSmtBuffer = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan StructureLookback = TimeSpan.FromMinutes(22);
    private static readonly TimeSpan MaxPostSmtFocus = TimeSpan.FromMinutes(70);
    private static readonly TimeSpan StoryBuffer = TimeSpan.FromMinutes(8);
    private readonly SwingDetector _swingDetector = new();

    public FocusedAnalysisResult Analyze(IReadOnlyList<Candle> nqCandles, SmtSignal signal, AgentSettings settings)
    {
        if (nqCandles.Count == 0)
        {
            return new FocusedAnalysisResult(signal.Time - PreSmtBuffer, signal.Time + StoryBuffer, [], "No NQ 1m candles available.");
        }

        var tolerance = Math.Max(settings.TickTolerance * settings.TickSize, settings.TickSize);
        var focusStart = GetFocusStart(signal);
        var analysisStart = focusStart - StructureLookback;
        var analysisEnd = signal.Time + MaxPostSmtFocus;
        var analysisCandles = nqCandles
            .Where(candle => candle.Time >= analysisStart && candle.Time <= analysisEnd)
            .OrderBy(candle => candle.Time)
            .ToList();

        if (analysisCandles.Count == 0)
        {
            return new FocusedAnalysisResult(focusStart, signal.Time + StoryBuffer, [], "No NQ 1m candles in the selected SMT window.");
        }

        var annotations = new List<FocusedChartAnnotation>();
        var direction = signal.Type;
        var bos = FindBos(analysisCandles, signal.Time, direction, settings.SwingStrength, tolerance);
        if (bos is not null)
        {
            annotations.Add(new FocusedChartAnnotation(
                FocusedAnnotationKind.Bos,
                direction,
                bos.Swing.Time,
                bos.BreakCandle.Time,
                bos.Swing.Price,
                bos.BreakCandle.Close,
                null,
                "BOS"));
        }

        var fvgs = DetectFvgs(analysisCandles, tolerance);
        var executionFvg = SelectExecutionFvg(fvgs, bos, direction);
        if (executionFvg is not null)
        {
            annotations.Add(ToAnnotation(executionFvg, FocusedAnnotationKind.Fvg, direction, "FVG"));
        }

        var ifvg = DetectIfvg(fvgs, analysisCandles, signal.Time, bos, direction, tolerance);
        if (ifvg is not null)
        {
            annotations.Add(ifvg);
        }

        var storyEnd = GetStoryEnd(signal, analysisCandles, annotations);
        var halfBox = BuildHalfBox(analysisCandles, signal, direction, focusStart, storyEnd);
        if (halfBox is not null)
        {
            annotations.Add(halfBox);

            var tradeBox = BuildTradeBox(analysisCandles, signal.Time, direction, halfBox, storyEnd);
            if (tradeBox is not null)
            {
                annotations.Add(tradeBox);
            }
        }

        storyEnd = GetStoryEnd(signal, analysisCandles, annotations);
        var windowEnd = MinTime(storyEnd + StoryBuffer, signal.Time + MaxPostSmtFocus);

        return new FocusedAnalysisResult(
            focusStart,
            windowEnd,
            annotations.OrderBy(annotation => annotation.StartTime).ThenBy(annotation => annotation.Kind).ToList(),
            BuildSummary(signal, bos, annotations));
    }

    private static DateTime GetFocusStart(SmtSignal signal)
    {
        var candidates = new List<DateTime> { signal.Time - PreSmtBuffer };
        if (signal.SetupType == SmtSetupType.HighLow && signal.NqPreviousSwingTime != default)
        {
            candidates.Add(signal.NqPreviousSwingTime - TimeSpan.FromMinutes(2));
        }

        if (signal.NqFvgStartTime is not null)
        {
            candidates.Add(signal.NqFvgStartTime.Value - TimeSpan.FromMinutes(2));
        }

        var earliest = candidates.Min();
        return earliest < signal.Time - TimeSpan.FromMinutes(35)
            ? signal.Time - TimeSpan.FromMinutes(35)
            : earliest;
    }

    private BosResult? FindBos(
        IReadOnlyList<Candle> candles,
        DateTime signalTime,
        SmtSignalType direction,
        int swingStrength,
        decimal tolerance)
    {
        var swings = _swingDetector.Detect(candles, Math.Max(1, swingStrength));
        for (var index = 0; index < candles.Count; index++)
        {
            var candle = candles[index];
            if (candle.Time <= signalTime)
            {
                continue;
            }

            var swingType = direction == SmtSignalType.Bearish ? SwingPointType.Low : SwingPointType.High;
            var swing = swings
                .Where(item => item.Type == swingType && item.CandleIndex < index && item.Time >= signalTime - PreSmtBuffer)
                .OrderByDescending(item => item.CandleIndex)
                .FirstOrDefault();
            if (swing is null)
            {
                continue;
            }

            var broke = direction == SmtSignalType.Bearish
                ? candle.Close < swing.Price - tolerance
                : candle.Close > swing.Price + tolerance;
            if (broke)
            {
                return new BosResult(swing, candle);
            }
        }

        return null;
    }

    private static FairValueGap? SelectExecutionFvg(
        IReadOnlyList<FairValueGap> fvgs,
        BosResult? bos,
        SmtSignalType direction)
    {
        if (bos is null)
        {
            return null;
        }

        var setupType = direction == SmtSignalType.Bearish ? FairValueGapType.Bearish : FairValueGapType.Bullish;
        return fvgs
            .Where(fvg => fvg.Type == setupType && fvg.EndTime >= bos.BreakCandle.Time)
            .OrderBy(fvg => fvg.StartTime)
            .FirstOrDefault();
    }

    private static FocusedChartAnnotation? DetectIfvg(
        IReadOnlyList<FairValueGap> fvgs,
        IReadOnlyList<Candle> candles,
        DateTime signalTime,
        BosResult? bos,
        SmtSignalType direction,
        decimal tolerance)
    {
        var sourceType = direction == SmtSignalType.Bearish ? FairValueGapType.Bullish : FairValueGapType.Bearish;
        var earliestInversion = bos?.BreakCandle.Time ?? signalTime;
        foreach (var fvg in fvgs
            .Where(fvg => fvg.Type == sourceType && fvg.StartTime >= signalTime && fvg.EndTime <= signalTime + MaxPostSmtFocus)
            .OrderBy(fvg => fvg.StartTime))
        {
            var inverted = candles.FirstOrDefault(candle =>
                candle.Time > fvg.EndTime &&
                candle.Time >= earliestInversion &&
                IsInversionCandle(candle, fvg, direction, candles, tolerance));
            if (inverted is null)
            {
                continue;
            }

            return new FocusedChartAnnotation(
                FocusedAnnotationKind.Ifvg,
                direction,
                fvg.StartTime,
                inverted.Time,
                fvg.Upper,
                fvg.Lower,
                null,
                direction == SmtSignalType.Bearish ? "Bearish IFVG" : "Bullish IFVG");
        }

        return null;
    }

    private static FocusedChartAnnotation? BuildHalfBox(
        IReadOnlyList<Candle> candles,
        SmtSignal signal,
        SmtSignalType direction,
        DateTime startTime,
        DateTime endTime)
    {
        var postSmt = candles.Where(candle => candle.Time >= signal.Time && candle.Time <= endTime).ToList();
        if (postSmt.Count == 0)
        {
            return null;
        }

        var level0 = signal.NqCurrentValue;
        var level1 = direction == SmtSignalType.Bearish
            ? postSmt.Min(candle => candle.Low)
            : postSmt.Max(candle => candle.High);
        var hasRange = direction == SmtSignalType.Bearish
            ? level1 < level0
            : level1 > level0;
        if (!hasRange)
        {
            return null;
        }

        var midpoint = (level0 + level1) / 2m;
        return new FocusedChartAnnotation(
            FocusedAnnotationKind.HalfBox,
            direction,
            startTime,
            endTime,
            level0,
            midpoint,
            level1,
            "0.5 Box");
    }

    private static FocusedChartAnnotation? BuildTradeBox(
        IReadOnlyList<Candle> candles,
        DateTime signalTime,
        SmtSignalType direction,
        FocusedChartAnnotation halfBox,
        DateTime endTime)
    {
        if (halfBox.SecondaryPrice is null)
        {
            return null;
        }

        var entry = halfBox.SecondaryPrice.Value;
        var trigger = candles.FirstOrDefault(candle =>
            candle.Time >= signalTime &&
            candle.Time <= endTime &&
            candle.Low <= entry &&
            candle.High >= entry);
        if (trigger is null)
        {
            return null;
        }

        var stop = halfBox.Price;
        var target = halfBox.TertiaryPrice ?? (direction == SmtSignalType.Bearish ? entry - 200m : entry + 200m);
        return new FocusedChartAnnotation(
            FocusedAnnotationKind.StopTakeProfit,
            direction,
            trigger.Time,
            endTime,
            entry,
            stop,
            target,
            "Entry");
    }

    private static IReadOnlyList<FairValueGap> DetectFvgs(IReadOnlyList<Candle> candles, decimal minGapSize)
    {
        var fvgs = new List<FairValueGap>();
        for (var index = 2; index < candles.Count; index++)
        {
            var first = candles[index - 2];
            var middle = candles[index - 1];
            var third = candles[index];

            if (first.High < third.Low && third.Low - first.High >= minGapSize && middle.Close > middle.Open)
            {
                fvgs.Add(new FairValueGap(third.Symbol, FairValueGapType.Bullish, index, first.Time, third.Time, first.High, third.Low));
            }

            if (first.Low > third.High && first.Low - third.High >= minGapSize && middle.Close < middle.Open)
            {
                fvgs.Add(new FairValueGap(third.Symbol, FairValueGapType.Bearish, index, first.Time, third.Time, third.High, first.Low));
            }
        }

        return fvgs;
    }

    private static bool IsInversionCandle(
        Candle candle,
        FairValueGap fvg,
        SmtSignalType direction,
        IReadOnlyList<Candle> candles,
        decimal tolerance)
    {
        var body = Math.Abs(candle.Close - candle.Open);
        var averageBody = GetAverageBody(candles, candle.Time, 10);
        var minimumBody = Math.Max(fvg.Upper - fvg.Lower, averageBody * 1.1m);

        if (direction == SmtSignalType.Bearish && fvg.Type == FairValueGapType.Bullish)
        {
            return candle.Close < candle.Open &&
                body >= minimumBody &&
                candle.Close < fvg.Lower - tolerance;
        }

        if (direction == SmtSignalType.Bullish && fvg.Type == FairValueGapType.Bearish)
        {
            return candle.Close > candle.Open &&
                body >= minimumBody &&
                candle.Close > fvg.Upper + tolerance;
        }

        return false;
    }

    private static decimal GetAverageBody(IReadOnlyList<Candle> candles, DateTime beforeTime, int count)
    {
        var sample = candles
            .Where(candle => candle.Time < beforeTime)
            .OrderByDescending(candle => candle.Time)
            .Take(count)
            .Select(candle => Math.Abs(candle.Close - candle.Open))
            .Where(body => body > 0m)
            .ToList();
        return sample.Count == 0 ? 1m : sample.Average();
    }

    private static DateTime GetStoryEnd(SmtSignal signal, IReadOnlyList<Candle> candles, IReadOnlyList<FocusedChartAnnotation> annotations)
    {
        var annotationEnd = annotations
            .Select(annotation => annotation.EndTime)
            .DefaultIfEmpty(signal.Time + TimeSpan.FromMinutes(45))
            .Max();
        var desiredEnd = new[] { signal.Time + TimeSpan.FromMinutes(45), annotationEnd }.Max();
        var availableEnd = candles
            .Where(candle => candle.Time >= signal.Time)
            .Select(candle => candle.Time)
            .DefaultIfEmpty(signal.Time)
            .Max();

        return MinTime(desiredEnd, availableEnd);
    }

    private static DateTime MinTime(DateTime first, DateTime second)
    {
        return first <= second ? first : second;
    }

    private static FocusedChartAnnotation ToAnnotation(FairValueGap fvg, FocusedAnnotationKind kind, SmtSignalType direction, string label)
    {
        return new FocusedChartAnnotation(kind, direction, fvg.StartTime, fvg.EndTime, fvg.Upper, fvg.Lower, null, label);
    }

    private static string BuildSummary(SmtSignal signal, BosResult? bos, IReadOnlyList<FocusedChartAnnotation> annotations)
    {
        var bosText = bos is null ? "BOS pending" : $"BOS at {bos.BreakCandle.Time:HH:mm}";
        var fvgCount = annotations.Count(annotation => annotation.Kind == FocusedAnnotationKind.Fvg);
        var ifvgCount = annotations.Count(annotation => annotation.Kind == FocusedAnnotationKind.Ifvg);
        return $"{signal.Type} NQ 1m focus | {bosText} | FVG {fvgCount} | IFVG {ifvgCount}";
    }

    private sealed record BosResult(SwingPoint Swing, Candle BreakCandle);
}
