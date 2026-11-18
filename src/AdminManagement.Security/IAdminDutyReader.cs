namespace AdminManagement.Security;

public interface IAdminDutyReader
{
    Task<bool> IsSuperAdminAsync(string tenantId, string objectId, CancellationToken cancellationToken);
}
