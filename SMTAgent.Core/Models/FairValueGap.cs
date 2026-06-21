namespace SMTAgent.Core.Models;

public sealed record FairValueGap(
    string Symbol,
    FairValueGapType Type,
    int CandleIndex,
    DateTime StartTime,
    DateTime EndTime,
    decimal Lower,
    decimal Upper)
{
    public string GapId => $"{Symbol}:{Type}:{StartTime:O}:{EndTime:O}:{Lower:0.########}:{Upper:0.########}";
}
