using AccessControl.Application;
using AdminManagement.Security;

namespace AccessControl.Service.Security;

public sealed class AccessControlAdminDutyReader(IAdminScopeAuthorizer authorizer) : IAdminDutyReader
{
    public Task<bool> IsSuperAdminAsync(string tenantId, string objectId, CancellationToken cancellationToken) =>
        authorizer.CanManageAsync(objectId, tenantId, null, true, cancellationToken);
}
