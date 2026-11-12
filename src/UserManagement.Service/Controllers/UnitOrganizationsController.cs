using Microsoft.AspNetCore.Mvc;
using UserManagement.Application;
using UserManagement.Contracts;

namespace UserManagement.Service.Controllers;

[ApiController]
[Route("v1/unit-organizations")]
public sealed class UnitOrganizationsController(UserManagementService service,
    ILogger<UnitOrganizationsController> logger) : ControllerBase
{
    [HttpGet("{id:guid}/users")]
    [ProducesResponseType(typeof(PagedResult<UserSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUsersAsync(Guid id, [FromQuery] string tenantId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        PagedResult<UserSummary>? result = await service.GetUsersPageAsync(tenantId, id, page, pageSize,
            cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/roles")]
    [ProducesResponseType(typeof(PagedResult<RoleInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRolesAsync(Guid id, [FromQuery] string tenantId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        PagedResult<RoleInfo>? result = await service.GetRolesPageAsync(tenantId, id, page, pageSize,
            cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(UnitOrganizationInfo), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateUnitOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        UnitOrganizationInfo organization = await service.CreateUnitOrganizationAsync(request, cancellationToken);
        logger.LogInformation("Administrator {Actor} created organization {UnitOrganizationId}; trace {TraceId}",
            HttpContext.Items["UserManagementActor"], organization.Id, HttpContext.TraceIdentifier);
        return Created($"/v1/unit-organizations/{organization.Id:D}?tenantId={Uri.EscapeDataString(organization.TenantId)}",
            organization);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UnitOrganizationInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync([FromQuery] string tenantId, CancellationToken cancellationToken) =>
        Ok(await service.GetUnitOrganizationsAsync(tenantId, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(UnitOrganizationInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, [FromQuery] string tenantId,
        CancellationToken cancellationToken)
    {
        UnitOrganizationInfo? organization = await service.GetUnitOrganizationAsync(tenantId, id, cancellationToken);
        return organization is null ? NotFound() : Ok(organization);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UnitOrganizationInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromQuery] string tenantId,
        [FromBody] UpdateUnitOrganizationRequest request, CancellationToken cancellationToken)
    {
        UnitOrganizationInfo? organization = await service.UpdateUnitOrganizationAsync(tenantId, id, request,
            cancellationToken);
        logger.LogInformation("Administrator {Actor} updated organization {UnitOrganizationId}; found {Found}; trace {TraceId}",
            HttpContext.Items["UserManagementActor"], id, organization is not null, HttpContext.TraceIdentifier);
        return organization is null ? NotFound() : Ok(organization);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, [FromQuery] string tenantId,
        CancellationToken cancellationToken)
    {
        bool deleted = await service.DeleteUnitOrganizationAsync(tenantId, id, cancellationToken);
        logger.LogInformation("Administrator {Actor} deleted organization {UnitOrganizationId}; deleted {Deleted}; trace {TraceId}",
            HttpContext.Items["UserManagementActor"], id, deleted, HttpContext.TraceIdentifier);
        return deleted ? NoContent() : NotFound();
    }
}
