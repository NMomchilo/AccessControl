using Microsoft.AspNetCore.Mvc;
using UserManagement.Application;
using UserManagement.Contracts;

namespace UserManagement.Service.Controllers;

[ApiController]
[Route("v1/roles")]
public sealed class RolesController(UserManagementService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RoleInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] string tenantId,
        [FromQuery] Guid? unitOrganizationId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default)
    {
        PagedResult<RoleInfo>? result = await service.GetRolesPageAsync(tenantId, unitOrganizationId,
            page, pageSize, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(RoleInfo), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        RoleInfo role = await service.CreateRoleAsync(request, cancellationToken);
        return Created($"/v1/roles?tenantId={Uri.EscapeDataString(role.TenantId)}&unitOrganizationId={role.UnitOrganizationId:D}", role);
    }
}
