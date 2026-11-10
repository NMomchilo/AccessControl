using UserManagement.Contracts;

namespace UserManagement.Client;

public interface IUserManagementClient
{
    Task<UserIdentityResponse> ResolveAsync(UserIdentityRequest request, CancellationToken cancellationToken);
}
