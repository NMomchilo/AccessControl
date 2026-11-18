using Microsoft.AspNetCore.Mvc;
using UserManagement.Application;
using UserManagement.Contracts;

namespace UserManagement.Service.Controllers;

[ApiController]
[Route("v1/admin/access")]
public sealed class AdminAccessController(UserManagementService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdminAccessInfo), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] string tenantId,
        [FromQuery] Guid? unitOrganizationId, CancellationToken cancellationToken)
    {
        string actorOid = VerifiedAdminIdentity.Oid(User, tenantId);
        return Ok(await service.GetAdminAccessAsync(tenantId, actorOid, unitOrganizationId, cancellationToken));
    }
}

