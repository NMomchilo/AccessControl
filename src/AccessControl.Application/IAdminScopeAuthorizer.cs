namespace AccessControl.Application;

public interface IAdminScopeAuthorizer
{
    Task<bool> CanManageAsync(string actorObjectId, string tenantId, Guid? unitOrganizationId,
        bool requireSuperAdmin, CancellationToken cancellationToken);
}
