using UserManagement.Domain;

namespace UserManagement.Application;

public interface IUserStore
{
    Task<ManagedUser> ResolveAsync(string objectId, string? email, string? displayName, CancellationToken cancellationToken);
    Task<ManagedUser?> GetAsync(string objectId, CancellationToken cancellationToken);
    Task<bool> ReplaceRolesAsync(string objectId, IReadOnlyList<string> roles, CancellationToken cancellationToken);
    Task<bool> SetActiveAsync(string objectId, bool isActive, CancellationToken cancellationToken);
    Task<long> GetRevisionAsync(CancellationToken cancellationToken);
}
