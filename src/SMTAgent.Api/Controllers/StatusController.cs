using Microsoft.AspNetCore.Mvc;
using SMTAgent.Api.Contracts;
using SMTAgent.Api.Services;

namespace SMTAgent.Api.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
    private readonly MarketRuntimeService _marketRuntime;

    public StatusController(MarketRuntimeService marketRuntime)
    {
        _marketRuntime = marketRuntime;
    }

    [HttpGet]
    public ActionResult<DataStatusDto> GetStatus()
    {
        return Ok(_marketRuntime.GetStatus());
    }
}
