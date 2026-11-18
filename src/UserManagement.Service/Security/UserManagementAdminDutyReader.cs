using AdminManagement.Security;
using UserManagement.Application;

namespace UserManagement.Service.Security;

public sealed class UserManagementAdminDutyReader(UserManagementService service) : IAdminDutyReader
{
    public async Task<bool> IsSuperAdminAsync(string tenantId, string objectId,
        CancellationToken cancellationToken)
    {
        var access = await service.GetAdminAccessAsync(tenantId, objectId, null, cancellationToken);
        return access.IsActive && access.IsSuperAdmin;
    }
}
