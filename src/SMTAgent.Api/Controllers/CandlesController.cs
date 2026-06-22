using Microsoft.AspNetCore.Mvc;
using SMTAgent.Api.Contracts;
using SMTAgent.Api.Services;

namespace SMTAgent.Api.Controllers;

[ApiController]
[Route("api/candles")]
public sealed class CandlesController : ControllerBase
{
    private readonly MarketRuntimeService _marketRuntime;

    public CandlesController(MarketRuntimeService marketRuntime)
    {
        _marketRuntime = marketRuntime;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<CandleDto>> GetCandles([FromQuery] string symbol = "ES", [FromQuery] string timeframe = "15m")
    {
        var normalized = symbol.Equals("NQ", StringComparison.OrdinalIgnoreCase) ? "NQ" : "ES";
        return Ok(_marketRuntime.GetCandles(normalized, timeframe));
    }
}
