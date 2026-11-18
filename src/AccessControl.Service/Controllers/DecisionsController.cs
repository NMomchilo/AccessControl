using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AccessControl.Service.Controllers;

[ApiController]
[Route("v1/decisions")]
public sealed class DecisionsController(AuthorizationService authorizationService, ILogger<DecisionsController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(DecisionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DecideAsync([FromBody] DecisionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            DecisionResponse decision = await authorizationService.DecideAsync(request, cancellationToken);
            logger.LogDebug("Decision API returned {Decision}; trace {TraceId}", decision.Permit ? "Permit" : "Deny", HttpContext.TraceIdentifier);
            return Ok(decision);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            InvalidOperationException or System.Text.Json.JsonException)
        {
            logger.LogError(exception, "Decision evaluation failed; trace {TraceId}", HttpContext.TraceIdentifier);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
