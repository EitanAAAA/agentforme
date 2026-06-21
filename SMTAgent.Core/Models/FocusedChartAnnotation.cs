namespace SMTAgent.Core.Models;

public sealed record FocusedChartAnnotation(
    FocusedAnnotationKind Kind,
    SmtSignalType Direction,
    DateTime StartTime,
    DateTime EndTime,
    decimal Price,
    decimal? SecondaryPrice,
    decimal? TertiaryPrice,
    string Label,
    bool IsTriggered = true);

