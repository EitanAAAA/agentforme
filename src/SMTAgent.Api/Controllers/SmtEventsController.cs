using Microsoft.AspNetCore.Mvc;
using SMTAgent.Api.Contracts;
using SMTAgent.Api.Services;

namespace SMTAgent.Api.Controllers;

[ApiController]
[Route("api/smt-events")]
public sealed class SmtEventsController : ControllerBase
{
    private readonly MarketRuntimeService _marketRuntime;

    public SmtEventsController(MarketRuntimeService marketRuntime)
    {
        _marketRuntime = marketRuntime;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<SmtEventDto>> GetEvents()
    {
        return Ok(_marketRuntime.GetSmtEvents());
    }

    [HttpGet("{id}")]
    public ActionResult<SmtEventDto> GetEvent(string id)
    {
        var smtEvent = _marketRuntime.GetSmtEvent(id);
        return smtEvent is null ? NotFound() : Ok(smtEvent);
    }

    [HttpGet("{id}/nq-1m-analysis")]
    public async Task<ActionResult<NqOneMinuteAnalysisDto>> GetNqOneMinuteAnalysis(string id, CancellationToken cancellationToken)
    {
        var analysis = await _marketRuntime.GetFocusedAnalysisAsync(id, cancellationToken);
        return analysis is null ? NotFound() : Ok(analysis);
    }
}
