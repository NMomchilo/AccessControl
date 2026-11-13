using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AccessControl.Service.Controllers;

[ApiController]
[Route("v1/policies")]
public sealed class PoliciesController(AuthorizationService authorizationService, PolicyCatalogService policyCatalog,
    ILogger<PoliciesController> logger) : ControllerBase
{
    [HttpGet("search")]
    [ProducesResponseType(typeof(PolicyPageResponse), StatusCodes.Status200OK)]
    public Task<PolicyPageResponse> SearchAsync([FromQuery] string tenantId,
        [FromQuery] Guid? unitOrganizationId, [FromQuery] string? module, [FromQuery] string? httpMethod,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        policyCatalog.SearchAsync(tenantId, unitOrganizationId, module, httpMethod, page, pageSize,
            cancellationToken);

    [HttpGet]
    [ProducesResponseType(typeof(PolicyRequest), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync([FromQuery] string tenantId, [FromQuery, BindRequired] Guid unitOrganizationId, [FromQuery] string module,
        [FromQuery] string httpMethod, [FromQuery] string routeTemplate, CancellationToken cancellationToken)
    {
        PolicyRequest? policy = await authorizationService.GetPolicyAsync(tenantId, unitOrganizationId, module, httpMethod, routeTemplate, cancellationToken);
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
