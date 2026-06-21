namespace SMTAgent.Core.Models;

public sealed record FocusedAnalysisResult(
    DateTime WindowStart,
    DateTime WindowEnd,
    IReadOnlyList<FocusedChartAnnotation> Annotations,
    string Summary);

