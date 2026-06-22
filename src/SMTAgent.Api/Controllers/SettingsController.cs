using Microsoft.AspNetCore.Mvc;
using SMTAgent.Api.Contracts;
using SMTAgent.Api.Services;

namespace SMTAgent.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly MarketRuntimeService _marketRuntime;

    public SettingsController(MarketRuntimeService marketRuntime)
    {
        _marketRuntime = marketRuntime;
    }

    [HttpGet]
    public ActionResult<AppSettingsDto> GetSettings()
    {
        return Ok(_marketRuntime.GetSettings());
    }

    [HttpPut]
    public ActionResult<AppSettingsDto> UpdateSettings(AppSettingsDto settings)
    {
        return Ok(_marketRuntime.UpdateSettings(settings));
    }
}
