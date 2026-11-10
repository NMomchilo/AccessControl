using Microsoft.AspNetCore.Mvc;
using UserManagement.Application;
using UserManagement.Contracts;

namespace UserManagement.Service.Controllers;

[ApiController]
[Route("v1/roles")]
public sealed class RolesController(UserManagementService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RoleInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] string tenantId, CancellationToken cancellationToken) =>
        Ok(await service.GetRolesAsync(tenantId, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(RoleInfo), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        RoleInfo role = await service.CreateRoleAsync(request, cancellationToken);
        return Created($"/v1/roles?tenantId={Uri.EscapeDataString(role.TenantId)}", role);
    }
}
