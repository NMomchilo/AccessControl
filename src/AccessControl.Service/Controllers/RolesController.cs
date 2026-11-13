using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AccessControl.Service.Controllers;

[ApiController]
[Route("v1/roles/entitlements")]
public sealed class RolesController(AuthorizationService authorizationService, ILogger<RolesController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(RoleEntitlementsRequest), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] string tenantId, [FromQuery, BindRequired] Guid unitOrganizationId,
        [FromQuery] Guid roleId, CancellationToken cancellationToken)
    {
        RoleEntitlementsRequest result = await authorizationService.GetRoleEntitlementsAsync(tenantId, unitOrganizationId, roleId, cancellationToken);
        logger.LogDebug("Role entitlements API read completed; count {Count}, trace {TraceId}", result.EntitlementIds.Count, HttpContext.TraceIdentifier);
        return Ok(result);
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplaceEntitlementsAsync([FromBody] RoleEntitlementsRequest request, CancellationToken cancellationToken)
    {
        await authorizationService.ReplaceRoleEntitlementsAsync(request, cancellationToken);
        logger.LogInformation("Role entitlements API update completed; trace {TraceId}", HttpContext.TraceIdentifier);
        return NoContent();
    }
}
