using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AccessControl.Service.Controllers;

[ApiController]
[Route("v1/policies")]
public sealed class PoliciesController(AuthorizationService authorizationService, ILogger<PoliciesController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PolicyRequest), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromQuery] string tenantId, [FromQuery] string module,
        [FromQuery] string httpMethod, [FromQuery] string routeTemplate, CancellationToken cancellationToken)
    {
        PolicyRequest? policy = await authorizationService.GetPolicyAsync(tenantId, module, httpMethod, routeTemplate, cancellationToken);
        logger.LogDebug("Policy API lookup completed; found {Found}, trace {TraceId}", policy is not null, HttpContext.TraceIdentifier);
        return policy is null ? NotFound() : Ok(policy);
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpsertAsync([FromBody] PolicyRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.UpsertPolicyAsync(request, cancellationToken);
        logger.LogInformation("Policy API upsert completed; trace {TraceId}", HttpContext.TraceIdentifier);
        return NoContent();
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAsync([FromBody] PolicyKeyRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.DeletePolicyAsync(request, cancellationToken);
        logger.LogInformation("Policy API delete completed; trace {TraceId}", HttpContext.TraceIdentifier);
        return NoContent();
    }
}
