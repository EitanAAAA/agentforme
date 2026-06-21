using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class NqFocusedAnalysisEngine
{
    private readonly SwingDetector _swingDetector = new();

    public FocusedAnalysisResult Analyze(IReadOnlyList<Candle> nqCandles, SmtSignal signal, AgentSettings settings)
    {
        if (nqCandles.Count == 0)
        {
            return new FocusedAnalysisResult(signal.Time.AddMinutes(-10), signal.Time.AddMinutes(10), [], "No NQ 1m candles available.");
        }

        var tolerance = settings.TickTolerance * settings.TickSize;
        var anchorStart = GetAnchorStart(signal);
        var analysisStart = anchorStart.AddMinutes(-10);
        var analysisEnd = signal.Time.AddMinutes(120);
        var analysisCandles = nqCandles
            .Where(candle => candle.Time >= analysisStart && candle.Time <= analysisEnd)
            .OrderBy(candle => candle.Time)
            .ToList();

        if (analysisCandles.Count == 0)
        {
            return new FocusedAnalysisResult(analysisStart, signal.Time.AddMinutes(10), [], "No NQ 1m candles in the selected SMT window.");
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

        var fvgs = DetectFvgs(analysisCandles);
        annotations.AddRange(SelectStoryFvgs(fvgs, signal, bos, direction));
        annotations.AddRange(DetectIfvgs(fvgs, analysisCandles, signal.Time, direction, tolerance));

        var halfBox = BuildHalfBox(analysisCandles, signal, direction, analysisStart);
        if (halfBox is not null)
        {
            annotations.Add(halfBox);

            var tradeBox = BuildTradeBox(analysisCandles, signal.Time, direction, halfBox);
            if (tradeBox is not null)
            {
                annotations.Add(tradeBox);
            }
        }

        var lastAnnotationTime = annotations
            .Select(annotation => annotation.EndTime)
            .DefaultIfEmpty(signal.Time)
            .Max();
        var windowStart = analysisStart;
        var windowEnd = new[] { signal.Time.AddMinutes(10), lastAnnotationTime.AddMinutes(10) }.Max();

        return new FocusedAnalysisResult(
            windowStart,
            windowEnd,
            annotations.OrderBy(annotation => annotation.StartTime).ThenBy(annotation => annotation.Kind).ToList(),
            BuildSummary(signal, bos, annotations));
    }

    private static DateTime GetAnchorStart(SmtSignal signal)
    {
        if (signal.SetupType == SmtSetupType.HighLow)
        {
            return signal.NqPreviousSwingTime == default ? signal.Time.AddMinutes(-15) : signal.NqPreviousSwingTime;
        }

        return signal.NqFvgStartTime ?? signal.Time.AddMinutes(-15);
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
                .Where(item => item.Type == swingType && item.CandleIndex < index)
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

    private static IReadOnlyList<FocusedChartAnnotation> SelectStoryFvgs(
        IReadOnlyList<FairValueGap> fvgs,
        SmtSignal signal,
        BosResult? bos,
        SmtSignalType direction)
    {
        var annotations = new List<FocusedChartAnnotation>();
        var setupType = direction == SmtSignalType.Bearish ? FairValueGapType.Bearish : FairValueGapType.Bullish;
        var setupFvg = fvgs
            .Where(fvg => fvg.Type == setupType && fvg.StartTime <= signal.Time && fvg.EndTime >= signal.Time.AddMinutes(-45))
            .OrderByDescending(fvg => fvg.EndTime)
            .FirstOrDefault();
        if (setupFvg is not null)
        {
            annotations.Add(ToAnnotation(setupFvg, FocusedAnnotationKind.Fvg, direction, "FVG"));
        }

        if (bos is not null)
        {
            var executionFvg = fvgs
                .Where(fvg => fvg.Type == setupType && fvg.StartTime >= bos.BreakCandle.Time)
                .OrderBy(fvg => fvg.StartTime)
                .FirstOrDefault();
            if (executionFvg is not null && executionFvg.GapId != setupFvg?.GapId)
            {
                annotations.Add(ToAnnotation(executionFvg, FocusedAnnotationKind.Fvg, direction, "Execution FVG"));
            }
        }

        return annotations;
    }

    private static IReadOnlyList<FocusedChartAnnotation> DetectIfvgs(
        IReadOnlyList<FairValueGap> fvgs,
        IReadOnlyList<Candle> candles,
        DateTime signalTime,
        SmtSignalType direction,
        decimal tolerance)
    {
        var annotations = new List<FocusedChartAnnotation>();
        foreach (var fvg in fvgs.Where(fvg => fvg.EndTime <= signalTime.AddMinutes(60)))
        {
            var inverted = candles.FirstOrDefault(candle =>
                candle.Time > fvg.EndTime &&
                candle.Time >= signalTime.AddMinutes(-60) &&
                IsInversionCandle(candle, fvg, tolerance));
            if (inverted is null)
            {
                continue;
            }

            annotations.Add(new FocusedChartAnnotation(
                FocusedAnnotationKind.Ifvg,
                direction,
                fvg.StartTime,
                inverted.Time,
                fvg.Upper,
                fvg.Lower,
                null,
                "IFVG"));
            break;
        }

        return annotations;
    }

    private static FocusedChartAnnotation? BuildHalfBox(
        IReadOnlyList<Candle> candles,
        SmtSignal signal,
        SmtSignalType direction,
        DateTime startTime)
    {
        var postSmt = candles.Where(candle => candle.Time >= signal.Time).ToList();
        if (postSmt.Count == 0)
        {
            return null;
        }

        var level0 = direction == SmtSignalType.Bearish ? signal.NqCurrentValue : signal.NqCurrentValue;
        var level1 = direction == SmtSignalType.Bearish
            ? postSmt.Min(candle => candle.Low)
            : postSmt.Max(candle => candle.High);
        if (level0 == level1)
        {
            return null;
        }

        var midpoint = (level0 + level1) / 2m;
        return new FocusedChartAnnotation(
            FocusedAnnotationKind.HalfBox,
            direction,
            startTime,
            postSmt[^1].Time,
            level0,
            midpoint,
            level1,
            "0.5 Entry");
    }

    private static FocusedChartAnnotation? BuildTradeBox(
        IReadOnlyList<Candle> candles,
        DateTime signalTime,
        SmtSignalType direction,
        FocusedChartAnnotation halfBox)
    {
        if (halfBox.SecondaryPrice is null)
        {
            return null;
        }

        var entry = halfBox.SecondaryPrice.Value;
        var trigger = candles.FirstOrDefault(candle =>
            candle.Time >= signalTime &&
            candle.Low <= entry &&
            candle.High >= entry);
        if (trigger is null)
        {
            return null;
        }

        var stop = direction == SmtSignalType.Bearish ? entry + 10m : entry - 10m;
        var target = direction == SmtSignalType.Bearish ? entry - 200m : entry + 200m;
        return new FocusedChartAnnotation(
            FocusedAnnotationKind.StopTakeProfit,
            direction,
            trigger.Time,
            candles[^1].Time,
            entry,
            stop,
            target,
            "Entry");
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
                fvgs.Add(new FairValueGap(third.Symbol, FairValueGapType.Bullish, index, first.Time, third.Time, first.High, third.Low));
            }

            if (first.Low > third.High)
            {
                fvgs.Add(new FairValueGap(third.Symbol, FairValueGapType.Bearish, index, first.Time, third.Time, third.High, first.Low));
            }
        }

        return fvgs;
    }

    private static bool IsInversionCandle(Candle candle, FairValueGap fvg, decimal tolerance)
    {
        if (fvg.Type == FairValueGapType.Bullish)
        {
            return candle.Close < candle.Open && candle.Close <= fvg.Upper - tolerance;
        }

        return candle.Close > candle.Open && candle.Close >= fvg.Lower + tolerance;
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
