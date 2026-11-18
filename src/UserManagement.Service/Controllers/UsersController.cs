using Microsoft.AspNetCore.Mvc;
using UserManagement.Application;
using UserManagement.Contracts;

namespace UserManagement.Service.Controllers;

[ApiController]
[Route("v1/users")]
public sealed class UsersController(UserManagementService service, IUserManagementAdminAuthorizer authorizer, ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UserSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllAsync([FromQuery] string tenantId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default) =>
        await GetAllScopedAsync(tenantId, page, pageSize, cancellationToken);

    private async Task<IActionResult> GetAllScopedAsync(string tenantId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        await authorizer.RequireSuperAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), cancellationToken);
        return Ok(await service.GetUsersPageAsync(tenantId, null, page, pageSize, cancellationToken));
    }

    [HttpGet("by-email")]
    [ProducesResponseType(typeof(PagedResult<UserSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByEmailAsync([FromQuery] string tenantId, [FromQuery] string email,
        [FromQuery] Guid? unitOrganizationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        await RequireScopeAsync(tenantId, unitOrganizationId, cancellationToken);
        PagedResult<UserSummary>? result = await service.GetUsersPageAsync(tenantId, unitOrganizationId,
            page, pageSize, cancellationToken, email);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("revision")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRevisionAsync(CancellationToken cancellationToken)
    {
        long revision = await service.GetRevisionAsync(cancellationToken);
        return Ok(revision);
    }

    [HttpPost("resolve")]
    [ProducesResponseType(typeof(UserIdentityResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveAsync([FromBody] UserIdentityRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            UserIdentityResponse response = await service.ResolveAsync(request, cancellationToken);
            logger.LogDebug("User resolution completed; active {IsActive}; trace {TraceId}",
                response.IsActive, HttpContext.TraceIdentifier);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{objectId}")]
    [ProducesResponseType(typeof(UserIdentityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string objectId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        UserIdentityResponse? response = await service.GetAsync(tenantId, unitOrganizationId, objectId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPut("{objectId}/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplaceRolesAsync(string objectId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, [FromBody] ReplaceRolesRequest request,
        CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        bool found = await service.ReplaceRolesAsync(tenantId, unitOrganizationId, objectId, request, cancellationToken);
        logger.LogInformation("Administrator {Actor} replaced roles; found {Found}; trace {TraceId}",
            HttpContext.Items["UserManagementActor"], found, HttpContext.TraceIdentifier);
        return found ? NoContent() : NotFound();
    }

    [HttpPut("{objectId}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActiveAsync(string objectId, [FromQuery] string tenantId, [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
    {
        await authorizer.RequireSuperAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), cancellationToken);
        bool found = await service.SetActiveAsync(tenantId, objectId, request, cancellationToken);
        logger.LogInformation("Administrator {Actor} changed user status; found {Found}; trace {TraceId}",
            HttpContext.Items["UserManagementActor"], found, HttpContext.TraceIdentifier);
        return found ? NoContent() : NotFound();
    }

    [HttpPost("{objectId}")]
    public async Task<IActionResult> CreateUserAsync(string objectId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, [FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        bool created = await service.CreateUserInOrganizationAsync(tenantId, unitOrganizationId, objectId,
            request.DisplayName, request.Email, cancellationToken, request.RoleIds);
        if (!created) return Conflict();
        return Created($"/v1/users/{objectId}?tenantId={Uri.EscapeDataString(tenantId)}&unitOrganizationId={unitOrganizationId:D}", null);
    }

    [HttpPut("{objectId}")]
    public async Task<IActionResult> UpdateUserAsync(string objectId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, [FromBody] UserProfileRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        if (await service.GetAsync(tenantId, unitOrganizationId, objectId, cancellationToken) is null)
            return NotFound();
        return await service.UpdateUserAsync(tenantId, objectId, request.DisplayName, request.Email, cancellationToken)
            ? NoContent() : NotFound();
    }

    [HttpDelete("{objectId}")]
    public async Task<IActionResult> DeleteUserAsync(string objectId, [FromQuery] string tenantId,
        CancellationToken cancellationToken)
    {
        await authorizer.RequireSuperAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), cancellationToken);
        return await service.DeleteUserAsync(tenantId, objectId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPut("{objectId}/memberships/{unitOrganizationId:guid}")]
    public async Task<IActionResult> AssignMembershipAsync(string objectId, Guid unitOrganizationId,
        [FromQuery] string tenantId, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        return await service.AssignMembershipAsync(tenantId, unitOrganizationId, objectId, cancellationToken)
            ? NoContent() : NotFound();
    }

    [HttpDelete("{objectId}/memberships/{unitOrganizationId:guid}")]
    public async Task<IActionResult> RevokeMembershipAsync(string objectId, Guid unitOrganizationId,
        [FromQuery] string tenantId, CancellationToken cancellationToken)
    {
        await authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId, cancellationToken);
        return await service.RevokeMembershipAsync(tenantId, unitOrganizationId, objectId, cancellationToken)
            ? NoContent() : NotFound();
    }

    [HttpPut("{objectId}/duties/super-admin")]
    public async Task<IActionResult> SetSuperAdminAsync(string objectId, [FromQuery] string tenantId,
        [FromBody] SetActiveRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequireSuperAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), cancellationToken);
        return await service.SetSuperAdminAsync(tenantId, objectId, request.IsActive, cancellationToken)
            ? NoContent() : NotFound();
    }

    [HttpPut("{objectId}/duties/unit-admin/{unitOrganizationId:guid}")]
    public async Task<IActionResult> SetUnitAdminAsync(string objectId, Guid unitOrganizationId,
        [FromQuery] string tenantId, [FromBody] SetActiveRequest request, CancellationToken cancellationToken)
    {
        await authorizer.RequireSuperAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), cancellationToken);
        return await service.SetUnitAdminAsync(tenantId, unitOrganizationId, objectId, request.IsActive, cancellationToken)
            ? NoContent() : NotFound();
    }

    private Task RequireScopeAsync(string tenantId, Guid? unitOrganizationId, CancellationToken cancellationToken) =>
        unitOrganizationId.HasValue
            ? authorizer.RequireAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), unitOrganizationId.Value, cancellationToken)
            : authorizer.RequireSuperAdminAsync(tenantId, VerifiedAdminIdentity.Oid(User, tenantId), cancellationToken);
}
