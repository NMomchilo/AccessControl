using AccessControl.Application;
using Microsoft.AspNetCore.Mvc;

namespace AccessControl.Service.Controllers;

public abstract class AdminControllerBase(IAdminScopeAuthorizer scopeAuthorizer) : ControllerBase
{
    protected async Task<bool> CanManageAsync(string tenantId, Guid? unitOrganizationId,
        CancellationToken cancellationToken)
    {
        string? actorObjectId = HttpContext.User.FindFirst("oid")?.Value;
        if (string.IsNullOrWhiteSpace(actorObjectId) || string.IsNullOrWhiteSpace(tenantId))
            return false;

        return await scopeAuthorizer.CanManageAsync(actorObjectId, tenantId, unitOrganizationId,
            !unitOrganizationId.HasValue || unitOrganizationId == Guid.Empty, cancellationToken);
    }

}
