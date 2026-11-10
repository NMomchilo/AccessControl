using UserManagement.Domain;

namespace UserManagement.Application;

public interface IUserStore
{
    Task<ManagedUser> ResolveAsync(string tenantId, string objectId, string? email, string? displayName, CancellationToken cancellationToken);
    Task<ManagedUser?> GetAsync(string tenantId, string objectId, CancellationToken cancellationToken);
    Task<bool> ReplaceRolesAsync(string tenantId, string objectId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken);
    Task<bool> SetActiveAsync(string tenantId, string objectId, bool isActive, CancellationToken cancellationToken);
    Task<ManagedRole> CreateRoleAsync(string tenantId, string code, string displayName, CancellationToken cancellationToken);
    Task<IReadOnlyList<ManagedRole>> GetRolesAsync(string tenantId, CancellationToken cancellationToken);
    Task<long> GetRevisionAsync(CancellationToken cancellationToken);
}
