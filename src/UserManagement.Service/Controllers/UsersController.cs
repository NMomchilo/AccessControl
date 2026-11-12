using Microsoft.AspNetCore.Mvc;
using UserManagement.Application;
using UserManagement.Contracts;

namespace UserManagement.Service.Controllers;

[ApiController]
[Route("v1/users")]
public sealed class UsersController(UserManagementService service, ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UserSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllAsync([FromQuery] string tenantId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100, CancellationToken cancellationToken = default) =>
        Ok(await service.GetUsersPageAsync(tenantId, null, page, pageSize, cancellationToken));

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
        UserIdentityResponse response = await service.ResolveAsync(request, cancellationToken);
        logger.LogDebug("User resolution completed; active {IsActive}; trace {TraceId}",
            response.IsActive, HttpContext.TraceIdentifier);
        return Ok(response);
    }

    [HttpGet("{objectId}")]
    [ProducesResponseType(typeof(UserIdentityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(string objectId, [FromQuery] string tenantId,
        [FromQuery] Guid unitOrganizationId, CancellationToken cancellationToken)
    {
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
        bool found = await service.SetActiveAsync(tenantId, objectId, request, cancellationToken);
        logger.LogInformation("Administrator {Actor} changed user status; found {Found}; trace {TraceId}",
            HttpContext.Items["UserManagementActor"], found, HttpContext.TraceIdentifier);
        return found ? NoContent() : NotFound();
    }
}
