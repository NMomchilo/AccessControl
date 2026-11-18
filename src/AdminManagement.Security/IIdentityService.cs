namespace AdminManagement.Security;

public interface IIdentityService
{
    string? GetUserEmail();
    Guid? GetUserId();
    Guid? GetClientId();
    bool IsInternalServiceCall();
    Task<bool> CheckIsSuperAdminAsync(string tenantId, CancellationToken cancellationToken = default);
}
