using SMTAgent.Core.Models;

namespace SMTAgent.Core.Engines;

public sealed class NqFocusedAnalysisEngine
{
    private static readonly TimeSpan PreSmtBuffer = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan StructureLookback = TimeSpan.FromMinutes(22);
    private static readonly TimeSpan MaxPostSmtFocus = TimeSpan.FromMinutes(70);
    private static readonly TimeSpan StoryBuffer = TimeSpan.FromMinutes(8);

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
        var smtAnnotation = BuildSmtAnnotation(signal, focusStart);
        if (smtAnnotation is not null)
        {
            annotations.Add(smtAnnotation);
        }

        var smtExtreme = BuildSmtExtremeAnnotation(analysisCandles, signal, direction);
        if (smtExtreme is not null)
        {
            annotations.Add(smtExtreme);
        }

        var bos = FindBos(analysisCandles, signal.Time, direction, settings.SwingStrength, tolerance);
        if (bos is not null)
        {
            annotations.Add(new FocusedChartAnnotation(
                FocusedAnnotationKind.Bos,
                direction,
                bos.Swing.Time,
                bos.BreakCandle.Time,
                bos.Swing.Price,
                bos.BreakPrice,
                null,
                "BOS"));
        }

        var fvgs = DetectFvgs(analysisCandles, tolerance);
        var setupFvg = SelectSetupFvg(fvgs, signal, bos, direction);
        if (setupFvg is not null)
        {
            annotations.Add(ToFvgAnnotation(setupFvg, FocusedAnnotationKind.Fvg, GetSetupFvgLabel(setupFvg)));
        }

        var ifvg = DetectIfvg(setupFvg, analysisCandles, bos, direction, tolerance);
        if (ifvg is not null)
        {
            annotations.Add(ifvg);
        }

        var storyEnd = GetStoryEnd(signal, analysisCandles, annotations);
        var halfBox = BuildHalfBox(analysisCandles, signal, direction, storyEnd, bos, ifvg);
        if (halfBox is not null)
        {
            annotations.Add(halfBox);

            var tradeBox = BuildTradeBox(analysisCandles, ifvg!.EndTime, direction, halfBox, storyEnd, settings);
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

    private static FocusedChartAnnotation? BuildSmtAnnotation(SmtSignal signal, DateTime fallbackStart)
    {
        if (signal.SetupType == SmtSetupType.HighLow)
        {
            var start = signal.NqPreviousSwingTime == default ? fallbackStart : signal.NqPreviousSwingTime;
            return new FocusedChartAnnotation(
                FocusedAnnotationKind.Smt,
                signal.Type,
                start,
                signal.Time,
                signal.NqPreviousSwingValue,
                signal.NqCurrentValue,
                null,
                "Selected SMT");
        }

        if (signal.NqFvgLower is not null &&
            signal.NqFvgUpper is not null &&
            signal.NqFvgStartTime is not null)
        {
            return new FocusedChartAnnotation(
                FocusedAnnotationKind.Smt,
                signal.Type,
                signal.NqFvgStartTime.Value,
                signal.Time,
                signal.NqFvgUpper.Value,
                signal.NqFvgLower.Value,
                null,
                "Selected SMT");
        }

        return null;
    }

    private static FocusedChartAnnotation? BuildSmtExtremeAnnotation(
        IReadOnlyList<Candle> candles,
        SmtSignal signal,
        SmtSignalType direction)
    {
        if (signal.SetupType != SmtSetupType.HighLow)
        {
            return null;
        }

        var postSmt = candles
            .Where(candle => candle.Time >= signal.Time)
            .OrderBy(candle => candle.Time)
            .ToList();
        if (postSmt.Count == 0)
        {
            return null;
        }

        var extreme = direction == SmtSignalType.Bearish
            ? postSmt.OrderByDescending(candle => candle.High).ThenBy(candle => candle.Time).First()
            : postSmt.OrderBy(candle => candle.Low).ThenBy(candle => candle.Time).First();
        var price = direction == SmtSignalType.Bearish ? extreme.High : extreme.Low;

        return new FocusedChartAnnotation(
            FocusedAnnotationKind.SmtExtreme,
            direction,
            extreme.Time,
            extreme.Time,
            price,
            null,
            null,
            "SMT point");
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
        var swings = DetectStructurePivots(candles);
        var signalIndex = candles
            .Select((candle, index) => new { candle.Time, index })
            .Where(item => item.Time <= signalTime)
            .OrderByDescending(item => item.Time)
            .Select(item => item.index)
            .FirstOrDefault();
        var swingType = direction == SmtSignalType.Bearish ? SwingPointType.Low : SwingPointType.High;
        var structureSwing = swings
            .Where(item =>
                item.Type == swingType &&
                item.Time < signalTime &&
                item.Time >= signalTime - StructureLookback &&
                item.CandleIndex + 1 <= signalIndex)
            .OrderByDescending(item => item.CandleIndex)
            .FirstOrDefault();
        if (structureSwing is null)
        {
            return null;
        }

        for (var index = 0; index < candles.Count; index++)
        {
            var candle = candles[index];
            if (candle.Time <= signalTime)
            {
                continue;
            }

            var broke = direction == SmtSignalType.Bearish
                ? candle.Low < structureSwing.Price - tolerance
                : candle.High > structureSwing.Price + tolerance;
            if (broke)
            {
                var breakPrice = direction == SmtSignalType.Bearish ? candle.Low : candle.High;
                return new BosResult(structureSwing, candle, breakPrice);
            }
        }

        return null;
    }

    private static FairValueGap? SelectSetupFvg(
        IReadOnlyList<FairValueGap> fvgs,
        SmtSignal signal,
        BosResult? bos,
        SmtSignalType direction)
    {
        if (bos is null)
        {
            return null;
        }

        var setupType = GetSourceFvgType(direction);
        var moveStart = signal.SetupType == SmtSetupType.HighLow && signal.NqPreviousSwingTime != default
            ? signal.NqPreviousSwingTime
            : signal.Time - PreSmtBuffer;

        var intoSmt = fvgs
            .Where(fvg => fvg.Type == setupType && fvg.EndTime >= moveStart && fvg.EndTime <= signal.Time)
            .OrderByDescending(fvg => fvg.EndTime)
            .FirstOrDefault();
        if (intoSmt is not null)
        {
            return intoSmt;
        }

        return fvgs
            .Where(fvg => fvg.Type == setupType && fvg.EndTime > signal.Time && fvg.EndTime <= bos.BreakCandle.Time)
            .OrderByDescending(fvg => fvg.EndTime)
            .FirstOrDefault();
    }

    private static FocusedChartAnnotation? DetectIfvg(
        FairValueGap? setupFvg,
        IReadOnlyList<Candle> candles,
        BosResult? bos,
        SmtSignalType direction,
        decimal tolerance)
    {
        if (setupFvg is null || bos is null)
        {
            return null;
        }

        var inverted = candles.FirstOrDefault(candle =>
            candle.Time > setupFvg.EndTime &&
            candle.Time >= bos.BreakCandle.Time &&
            IsInversionCandle(candle, setupFvg, direction, tolerance));
        if (inverted is null)
        {
            return null;
        }

        return new FocusedChartAnnotation(
            FocusedAnnotationKind.Ifvg,
            direction,
            setupFvg.StartTime,
            inverted.Time,
            setupFvg.Upper,
            setupFvg.Lower,
            null,
            direction == SmtSignalType.Bearish ? "Bearish IFVG" : "Bullish IFVG");
    }

    private static FocusedChartAnnotation? BuildHalfBox(
        IReadOnlyList<Candle> candles,
        SmtSignal signal,
        SmtSignalType direction,
        DateTime endTime,
        BosResult? bos,
        FocusedChartAnnotation? ifvg)
    {
        if (bos is null || ifvg is null)
        {
            return null;
        }

        var sweepWindow = candles
            .Where(candle => candle.Time >= signal.Time && candle.Time <= bos.BreakCandle.Time)
            .ToList();
        var displacementWindow = candles
            .Where(candle => candle.Time >= bos.BreakCandle.Time && candle.Time <= ifvg.EndTime)
            .ToList();
        if (sweepWindow.Count == 0 || displacementWindow.Count == 0)
        {
            return null;
        }

        var smtExtreme = direction == SmtSignalType.Bearish
            ? Math.Max(signal.NqCurrentValue, sweepWindow.Max(candle => candle.High))
            : Math.Min(signal.NqCurrentValue, sweepWindow.Min(candle => candle.Low));
        var displacementExtreme = direction == SmtSignalType.Bearish
            ? displacementWindow.Min(candle => candle.Low)
            : displacementWindow.Max(candle => candle.High);
        var level0 = direction == SmtSignalType.Bearish ? displacementExtreme : smtExtreme;
        var level1 = direction == SmtSignalType.Bearish ? smtExtreme : displacementExtreme;
        if (level1 <= level0)
        {
            return null;
        }

        var midpoint = (level0 + level1) / 2m;
        return new FocusedChartAnnotation(
            FocusedAnnotationKind.HalfBox,
            direction,
            signal.Time,
            endTime,
            level0,
            midpoint,
            level1,
            "0.5 Box");
    }

    private static FocusedChartAnnotation? BuildTradeBox(
        IReadOnlyList<Candle> candles,
        DateTime availableFrom,
        SmtSignalType direction,
        FocusedChartAnnotation halfBox,
        DateTime endTime,
        AgentSettings settings)
    {
        if (halfBox.SecondaryPrice is null || halfBox.TertiaryPrice is null)
        {
            return null;
        }

        var entry = halfBox.SecondaryPrice.Value;
        var trigger = candles.FirstOrDefault(candle =>
            candle.Time >= availableFrom &&
            candle.Time <= endTime &&
            candle.Low <= entry &&
            candle.High >= entry);
        if (trigger is null)
        {
            return null;
        }

        var riskBuffer = Math.Max(settings.RiskBufferPoints, settings.TickSize);
        var invalidation = direction == SmtSignalType.Bearish ? halfBox.TertiaryPrice.Value : halfBox.Price;
        var stop = direction == SmtSignalType.Bearish
            ? invalidation + riskBuffer
            : invalidation - riskBuffer;
        var target = direction == SmtSignalType.Bearish ? halfBox.Price : halfBox.TertiaryPrice.Value;
        return new FocusedChartAnnotation(
            FocusedAnnotationKind.StopTakeProfit,
            direction,
            trigger.Time,
            endTime,
            entry,
            stop,
            target,
            "Visual entry");
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

    private static IReadOnlyList<SwingPoint> DetectStructurePivots(IReadOnlyList<Candle> candles)
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

    private static bool IsInversionCandle(
        Candle candle,
        FairValueGap fvg,
        SmtSignalType direction,
        decimal tolerance)
    {
        if (direction == SmtSignalType.Bearish && fvg.Type == FairValueGapType.Bullish)
        {
            return candle.Close < candle.Open &&
                candle.Close < fvg.Lower - tolerance;
        }

        if (direction == SmtSignalType.Bullish && fvg.Type == FairValueGapType.Bearish)
        {
            return candle.Close > candle.Open &&
                candle.Close > fvg.Upper + tolerance;
        }

        return false;
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

    private static FocusedChartAnnotation ToFvgAnnotation(FairValueGap fvg, FocusedAnnotationKind kind, string label)
    {
        var direction = fvg.Type == FairValueGapType.Bullish ? SmtSignalType.Bullish : SmtSignalType.Bearish;
        return ToAnnotation(fvg, kind, direction, label);
    }

    private static FairValueGapType GetSourceFvgType(SmtSignalType direction)
    {
        return direction == SmtSignalType.Bearish ? FairValueGapType.Bullish : FairValueGapType.Bearish;
    }

    private static string GetSetupFvgLabel(FairValueGap fvg)
    {
        return fvg.Type == FairValueGapType.Bullish ? "Bullish FVG" : "Bearish FVG";
    }

    private static string BuildSummary(SmtSignal signal, BosResult? bos, IReadOnlyList<FocusedChartAnnotation> annotations)
    {
        var bosText = bos is null ? "BOS pending" : $"BOS at {bos.BreakCandle.Time:HH:mm}";
        var fvgCount = annotations.Count(annotation => annotation.Kind == FocusedAnnotationKind.Fvg);
        var ifvgCount = annotations.Count(annotation => annotation.Kind == FocusedAnnotationKind.Ifvg);
        return $"{signal.Type} NQ 1m focus | {bosText} | FVG {fvgCount} | IFVG {ifvgCount}";
    }

    private sealed record BosResult(SwingPoint Swing, Candle BreakCandle, decimal BreakPrice);
}
