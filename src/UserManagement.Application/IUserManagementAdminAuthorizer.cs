using UserManagement.Contracts;

namespace UserManagement.Application;

public interface IUserManagementAdminAuthorizer
{
    Task RequireAdminAsync(string tenantId, string actorOid, Guid unitOrganizationId,
        CancellationToken cancellationToken);
    Task RequireSuperAdminAsync(string tenantId, string actorOid, CancellationToken cancellationToken);
    Task<AdminAccessInfo> GetAccessAsync(string tenantId, string actorOid, CancellationToken cancellationToken);
}

public sealed class UserManagementAdminAuthorizer(UserManagementService service) : IUserManagementAdminAuthorizer
{
    public Task RequireAdminAsync(string tenantId, string actorOid, Guid unitOrganizationId,
        CancellationToken cancellationToken) => service.RequireAdminAsync(tenantId, actorOid, unitOrganizationId,
        cancellationToken);

    public Task RequireSuperAdminAsync(string tenantId, string actorOid, CancellationToken cancellationToken) =>
        service.RequireSuperAdminAsync(tenantId, actorOid, cancellationToken);

    public Task<AdminAccessInfo> GetAccessAsync(string tenantId, string actorOid, CancellationToken cancellationToken) =>
        service.GetAdminAccessAsync(tenantId, actorOid, null, cancellationToken);
}
