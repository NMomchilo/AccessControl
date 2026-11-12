using AccessControl.Application;
using AccessControl.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AccessControl.Service.Controllers;

[ApiController]
[Route("v1")]
public sealed class EntitlementsController(EntitlementCatalogService catalog) : ControllerBase
{
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
