using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AccessControl.Service.Controllers;

[ApiController]
[Route("v1")]
public sealed class EntitlementsController(EntitlementCatalogService catalog, ILogger<EntitlementsController> logger) : ControllerBase
{
    [HttpPost("entitlements")]
    [ProducesResponseType(typeof(EntitlementInfo), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateEntitlementRequest request,
        CancellationToken cancellationToken)
    {
        EntitlementInfo created = await catalog.CreateAsync(request, cancellationToken);
        logger.LogInformation("Administrator created entitlement {EntitlementId}; trace {TraceId}",
            created.Id, HttpContext.TraceIdentifier);
        return Created($"/v1/entitlements?tenantId={Uri.EscapeDataString(created.TenantId)}&unitOrganizationId={created.UnitOrganizationId:D}", created);
    }

    [HttpPut("entitlements/{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveAsync(Guid id, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, [FromBody] SetEntitlementActiveRequest request,
        CancellationToken cancellationToken)
    {
        bool found = await catalog.SetActiveAsync(tenantId, unitOrganizationId, id, request.IsActive, cancellationToken);
        logger.LogInformation("Administrator set entitlement {EntitlementId} active status to {IsActive}; found {Found}; trace {TraceId}",
            id, request.IsActive, found, HttpContext.TraceIdentifier);
        return found ? NoContent() : NotFound();
    }

    [HttpPut("entitlements/{id:guid}/display-name")]
    [ProducesResponseType(typeof(EntitlementInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDisplayNameAsync(Guid id, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, [FromBody] UpdateEntitlementDisplayNameRequest request,
        CancellationToken cancellationToken)
    {
        EntitlementInfo? updated = await catalog.UpdateDisplayNameAsync(tenantId, unitOrganizationId, id,
            request.DisplayName, cancellationToken);
        logger.LogInformation("Administrator updated entitlement {EntitlementId} display name; found {Found}; trace {TraceId}",
            id, updated is not null, HttpContext.TraceIdentifier);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("entitlements")]
    [ProducesResponseType(typeof(EntitlementPageResponse), StatusCodes.Status200OK)]
    public Task<EntitlementPageResponse> ListAsync([FromQuery] string tenantId, [FromQuery] int? page,
        [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        catalog.ListAsync(tenantId, null, page ?? 1, pageSize ?? 100, cancellationToken);

    [HttpGet("unit-organizations/{id:guid}/entitlements")]
    [ProducesResponseType(typeof(EntitlementPageResponse), StatusCodes.Status200OK)]
    public Task<EntitlementPageResponse> ListForOrganizationAsync(Guid id, [FromQuery] string tenantId,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        catalog.ListAsync(tenantId, id, page ?? 1, pageSize ?? 100, cancellationToken);
}
