using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AccessControl.Service.Controllers;

[ApiController]
[Route("v1/roles/entitlements")]
public sealed class RolesController(AuthorizationService authorizationService, IAdminScopeAuthorizer scopeAuthorizer,
    ILogger<RolesController> logger) : AdminControllerBase(scopeAuthorizer)
{
    [HttpGet]
    [ProducesResponseType(typeof(RoleEntitlementsRequest), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] string tenantId, [FromQuery, BindRequired] Guid unitOrganizationId,
        [FromQuery] Guid roleId, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(tenantId, unitOrganizationId, cancellationToken)) return Forbid();
        RoleEntitlementsRequest result = await authorizationService.GetRoleEntitlementsAsync(tenantId, unitOrganizationId, roleId, cancellationToken);
        logger.LogDebug("Role entitlements API read completed; count {Count}, trace {TraceId}", result.EntitlementIds.Count, HttpContext.TraceIdentifier);
        return Ok(result);
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReplaceEntitlementsAsync([FromBody] RoleEntitlementsRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !await CanManageAsync(request.TenantId, request.UnitOrganizationId, cancellationToken)) return Forbid();
        await authorizationService.ReplaceRoleEntitlementsAsync(request, cancellationToken);
        logger.LogInformation("Role entitlements API update completed; trace {TraceId}", HttpContext.TraceIdentifier);
        return NoContent();
    }
}
