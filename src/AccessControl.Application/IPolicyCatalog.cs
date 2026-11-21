using AccessControl.Contracts;

namespace AccessControl.Application;

public interface IPolicyCatalog
{
    Task<PolicyPageResponse> SearchAsync(string tenantId, Guid? unitOrganizationId,
        string? httpMethod, int page, int pageSize, CancellationToken cancellationToken);
}
