using Microsoft.AspNetCore.Mvc;
using UserManagement.Application;
using UserManagement.Contracts;

namespace UserManagement.Service.Controllers;

[ApiController]
[Route("v1/roles")]
public sealed class RolesController(UserManagementService service, IUserManagementAdminAuthorizer authorizer, ILogger<RolesController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RoleInfo>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] string tenantId,
        [FromQuery] Guid? unitOrganizationId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default)
    {
        await RequireScopeAsync(tenantId, unitOrganizationId, cancellationToken);
        PagedResult<RoleInfo>? result = await service.GetRolesPageAsync(tenantId, unitOrganizationId,
            page, pageSize, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{roleId:guid}")]
    [ProducesResponseType(typeof(RoleInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOneAsync(Guid roleId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        RoleInfo? role = await service.GetRoleAsync(tenantId, unitOrganizationId, roleId, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpPost]
    [ProducesResponseType(typeof(RoleInfo), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(request.TenantId, VerifiedAdminIdentity.Oid(User, request.TenantId),
            request.UnitOrganizationId, cancellationToken);
        RoleInfo role = await service.CreateRoleAsync(request, cancellationToken);
        return Created($"/v1/roles?tenantId={Uri.EscapeDataString(role.TenantId)}&unitOrganizationId={role.UnitOrganizationId:D}", role);
    }

    [HttpPut("{roleId:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveAsync(Guid roleId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, [FromBody] SetActiveRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        bool found = await service.SetRoleActiveAsync(tenantId, unitOrganizationId, roleId, request, cancellationToken);
        logger.LogInformation("Administrator {Actor} set role {RoleId} active status to {IsActive}; found {Found}; trace {TraceId}",
            HttpContext.Items["UserManagementActor"], roleId, request.IsActive, found, HttpContext.TraceIdentifier);
        return found ? NoContent() : NotFound();
    }

    [HttpPut("{roleId:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid roleId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        RoleInfo? role = await service.UpdateRoleAsync(tenantId, unitOrganizationId, roleId, request.DisplayName,
            cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpDelete("{roleId:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid roleId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        return await service.DeleteRoleAsync(tenantId, unitOrganizationId, roleId, cancellationToken)
            ? NoContent() : NotFound();
    }

    private Task RequireScopeAsync(string tenantId, Guid? unitOrganizationId, CancellationToken cancellationToken) =>
        unitOrganizationId.HasValue
            ? authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId.Value, cancellationToken)
            : authorizer.RequireSuperAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), cancellationToken);
}
