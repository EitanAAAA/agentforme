namespace SMTAgent.Api.Contracts;

public sealed record CandleDto(
    string Symbol,
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

public sealed record SwingPointDto(
    string Symbol,
    DateTime Timestamp,
    decimal Price,
    string Type,
    int CandleIndex);

public sealed record ChartAnnotationDto(
    string Kind,
    string Direction,
    DateTime StartTimestamp,
    DateTime EndTimestamp,
    decimal Price,
    decimal? SecondaryPrice,
    decimal? TertiaryPrice,
    string Label,
    bool IsTriggered);

public sealed record BosSignalDto(
    DateTime SwingTimestamp,
    DateTime BreakTimestamp,
    decimal SwingPrice,
    decimal BreakClose,
    string Direction,
    string Label);

public sealed record FvgZoneDto(
    DateTime StartTimestamp,
    DateTime EndTimestamp,
    decimal Lower,
    decimal Upper,
    string Direction,
    string Label);

public sealed record IfvgZoneDto(
    DateTime StartTimestamp,
    DateTime EndTimestamp,
    decimal Lower,
    decimal Upper,
    string Direction,
    string Label);

public sealed record HalfBoxDto(
    DateTime StartTimestamp,
    DateTime EndTimestamp,
    decimal Level0,
    decimal Level05,
    decimal Level1,
    string Direction);

public sealed record MockTradeBoxDto(
    DateTime StartTimestamp,
    DateTime EndTimestamp,
    decimal Entry,
    decimal StopLoss,
    decimal TakeProfit,
    string Direction,
    bool IsTriggered);

public sealed record SmtEventDto(
    string Id,
    DateTime Timestamp,
    string SetupType,
    string Direction,
    string Status,
    string LeaderSymbol,
    string FailedSymbol,
    string Reason,
    string WorkflowState,
    string CalculationSummary,
    string DetectionMode,
    decimal EsCurrentValue,
    decimal NqCurrentValue,
    DateTime EsCurrentTimestamp,
    DateTime NqCurrentTimestamp,
    decimal EsPreviousSwingValue,
    decimal NqPreviousSwingValue,
    DateTime EsPreviousSwingTimestamp,
    DateTime NqPreviousSwingTimestamp,
    decimal? EsFvgLower,
    decimal? EsFvgUpper,
    DateTime? EsFvgStartTimestamp,
    DateTime? EsFvgEndTimestamp,
    decimal? NqFvgLower,
    decimal? NqFvgUpper,
    DateTime? NqFvgStartTimestamp,
    DateTime? NqFvgEndTimestamp);

public sealed record SmtEventCanceledDto(
    string Id,
    string Reason);

public sealed record NqOneMinuteAnalysisDto(
    string SmtEventId,
    DateTime WindowStart,
    DateTime WindowEnd,
    IReadOnlyList<CandleDto> Candles,
    IReadOnlyList<BosSignalDto> BosSignals,
    IReadOnlyList<FvgZoneDto> FvgZones,
    IReadOnlyList<IfvgZoneDto> IfvgZones,
    HalfBoxDto? HalfBox,
    MockTradeBoxDto? MockTradeBox,
    IReadOnlyList<ChartAnnotationDto> Annotations,
    string Summary);

public sealed record AppSettingsDto(
    string Timeframe,
    string DetectionMode,
    int SwingStrength,
    int RefreshRateSeconds,
    int AlertCooldownMinutes,
    int AlertCooldownCandles,
    int TickTolerance,
    decimal TickSize,
    decimal RiskBufferPoints,
    bool ShowBullishSmt,
    bool ShowBearishSmt,
    string DataProvider);

public sealed record DataStatusDto(
    string Status,
    string DataStatus,
    string DataProvider,
    DateTime? LastUpdated,
    string Message);
