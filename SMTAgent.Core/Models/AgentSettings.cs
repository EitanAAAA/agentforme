namespace SMTAgent.Core.Models;

public sealed class AgentSettings
{
    public int SwingStrength { get; set; } = 2;
    public DetectionMode DetectionMode { get; set; } = DetectionMode.Both;
    public TimeSpan AlertCooldown { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan RefreshRate { get; set; } = TimeSpan.FromSeconds(60);
    public string Timeframe { get; set; } = "15m";
    public bool ShowBullishSmt { get; set; } = true;
    public bool ShowBearishSmt { get; set; } = true;
    public DataProviderMode DataProvider { get; set; } = DataProviderMode.YahooFinance;
}
