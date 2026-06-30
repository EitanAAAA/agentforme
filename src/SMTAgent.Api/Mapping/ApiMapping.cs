using SMTAgent.Api.Contracts;
using SMTAgent.Core.Models;

namespace SMTAgent.Api.Mapping;

public static class ApiMapping
{
    public static CandleDto ToDto(this Candle candle)
    {
        return new CandleDto(candle.Symbol, candle.Time, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
    }

    public static SwingPointDto ToDto(this SwingPoint swing)
    {
        return new SwingPointDto(swing.Symbol, swing.Time, swing.Price, swing.Type.ToString(), swing.CandleIndex);
    }

    public static SmtEventDto ToDto(this SmtSignal signal)
    {
        return new SmtEventDto(
            signal.SignalId,
            signal.Time,
            signal.SetupType.ToString(),
            signal.Type.ToString(),
            signal.Status.ToString(),
            signal.LeaderSymbol,
            signal.FailedSymbol,
            signal.Reason,
            signal.WorkflowState,
            signal.CalculationSummary,
            signal.DetectionMode.ToString(),
            signal.EsCurrentValue,
            signal.NqCurrentValue,
            signal.EsCurrentTime,
            signal.NqCurrentTime,
            signal.EsPreviousSwingValue,
            signal.NqPreviousSwingValue,
            signal.EsPreviousSwingTime,
            signal.NqPreviousSwingTime,
            signal.EsFvgLower,
            signal.EsFvgUpper,
            signal.EsFvgStartTime,
            signal.EsFvgEndTime,
            signal.NqFvgLower,
            signal.NqFvgUpper,
            signal.NqFvgStartTime,
            signal.NqFvgEndTime);
    }

    public static ChartAnnotationDto ToDto(this FocusedChartAnnotation annotation)
    {
        return new ChartAnnotationDto(
            annotation.Kind.ToString(),
            annotation.Direction.ToString(),
            annotation.StartTime,
            annotation.EndTime,
            annotation.Price,
            annotation.SecondaryPrice,
            annotation.TertiaryPrice,
            annotation.Label,
            annotation.IsTriggered);
    }

    public static AppSettingsDto ToDto(this AgentSettings settings)
    {
        return new AppSettingsDto(
            settings.Timeframe,
            settings.DetectionMode.ToString(),
            settings.SwingStrength,
            (int)settings.RefreshRate.TotalSeconds,
            (int)settings.AlertCooldown.TotalMinutes,
            settings.AlertCooldownCandles,
            settings.TickTolerance,
            settings.TickSize,
            settings.RiskBufferPoints,
            settings.ShowBullishSmt,
            settings.ShowBearishSmt,
            settings.DataProvider.ToString());
    }

    public static void Apply(this AgentSettings settings, AppSettingsDto dto)
    {
        settings.Timeframe = NormalizeTimeframe(dto.Timeframe);
        settings.SwingStrength = Math.Clamp(dto.SwingStrength, 1, 6);
        settings.RefreshRate = TimeSpan.FromSeconds(Math.Clamp(dto.RefreshRateSeconds, 15, 300));
        settings.AlertCooldown = TimeSpan.FromMinutes(Math.Clamp(dto.AlertCooldownMinutes, 0, 30));
        settings.AlertCooldownCandles = Math.Clamp(dto.AlertCooldownCandles, 0, 50);
        settings.TickTolerance = Math.Clamp(dto.TickTolerance, 0, 10);
        settings.TickSize = dto.TickSize <= 0 ? 0.25m : dto.TickSize;
        settings.RiskBufferPoints = Math.Clamp(dto.RiskBufferPoints, settings.TickSize, 100m);
        settings.ShowBullishSmt = dto.ShowBullishSmt;
        settings.ShowBearishSmt = dto.ShowBearishSmt;

        if (Enum.TryParse<DetectionMode>(dto.DetectionMode, true, out var detectionMode))
        {
            settings.DetectionMode = detectionMode;
        }

        if (Enum.TryParse<DataProviderMode>(dto.DataProvider, true, out var dataProvider))
        {
            settings.DataProvider = dataProvider;
        }
    }

    public static DataStatusDto ToStatusDto(
        AgentStatus status,
        DataConnectionStatus dataStatus,
        DataProviderMode dataProvider,
        DateTime? lastUpdated,
        string? message)
    {
        return new DataStatusDto(
            status.ToString(),
            dataStatus.ToString(),
            dataProvider.ToString(),
            lastUpdated,
            string.IsNullOrWhiteSpace(message) ? "DELAYED DATA - live updating view" : message);
    }

    public static NqOneMinuteAnalysisDto ToDto(
        this FocusedAnalysisResult result,
        string eventId,
        IReadOnlyList<Candle> focusedCandles)
    {
        var annotations = result.Annotations.Select(annotation => annotation.ToDto()).ToList();
        var bos = result.Annotations
            .Where(annotation => annotation.Kind == FocusedAnnotationKind.Bos && annotation.SecondaryPrice is not null)
            .Select(annotation => new BosSignalDto(
                annotation.StartTime,
                annotation.EndTime,
                annotation.Price,
                annotation.SecondaryPrice!.Value,
                annotation.Direction.ToString(),
                annotation.Label))
            .ToList();
        var fvgs = result.Annotations
            .Where(annotation => annotation.Kind == FocusedAnnotationKind.Fvg && annotation.SecondaryPrice is not null)
            .Select(annotation => ToFvgDto(annotation))
            .ToList();
        var ifvgs = result.Annotations
            .Where(annotation => annotation.Kind == FocusedAnnotationKind.Ifvg && annotation.SecondaryPrice is not null)
            .Select(annotation => ToIfvgDto(annotation))
            .ToList();
        var halfBox = result.Annotations
            .Where(annotation => annotation.Kind == FocusedAnnotationKind.HalfBox && annotation.SecondaryPrice is not null && annotation.TertiaryPrice is not null)
            .Select(annotation => new HalfBoxDto(
                annotation.StartTime,
                annotation.EndTime,
                annotation.Price,
                annotation.SecondaryPrice!.Value,
                annotation.TertiaryPrice!.Value,
                annotation.Direction.ToString()))
            .FirstOrDefault();
        var tradeBox = result.Annotations
            .Where(annotation => annotation.Kind == FocusedAnnotationKind.StopTakeProfit && annotation.SecondaryPrice is not null && annotation.TertiaryPrice is not null)
            .Select(annotation => new MockTradeBoxDto(
                annotation.StartTime,
                annotation.EndTime,
                annotation.Price,
                annotation.SecondaryPrice!.Value,
                annotation.TertiaryPrice!.Value,
                annotation.Direction.ToString(),
                annotation.IsTriggered))
            .FirstOrDefault();

        return new NqOneMinuteAnalysisDto(
            eventId,
            result.WindowStart,
            result.WindowEnd,
            focusedCandles.Select(candle => candle.ToDto()).ToList(),
            bos,
            fvgs,
            ifvgs,
            halfBox,
            tradeBox,
            annotations,
            result.Summary);
    }

    private static FvgZoneDto ToFvgDto(FocusedChartAnnotation annotation)
    {
        var lower = Math.Min(annotation.Price, annotation.SecondaryPrice!.Value);
        var upper = Math.Max(annotation.Price, annotation.SecondaryPrice.Value);
        return new FvgZoneDto(annotation.StartTime, annotation.EndTime, lower, upper, annotation.Direction.ToString(), annotation.Label);
    }

    private static IfvgZoneDto ToIfvgDto(FocusedChartAnnotation annotation)
    {
        var lower = Math.Min(annotation.Price, annotation.SecondaryPrice!.Value);
        var upper = Math.Max(annotation.Price, annotation.SecondaryPrice.Value);
        return new IfvgZoneDto(annotation.StartTime, annotation.EndTime, lower, upper, annotation.Direction.ToString(), annotation.Label);
    }

    private static string NormalizeTimeframe(string? timeframe)
    {
        return timeframe switch
        {
            "1m" or "5m" or "15m" or "30s" => timeframe,
            _ => "15m"
        };
    }
}
