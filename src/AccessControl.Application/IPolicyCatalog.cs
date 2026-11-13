using AccessControl.Contracts;

namespace AccessControl.Application;

public interface IPolicyCatalog
{
    Task<PolicyPageResponse> SearchAsync(string tenantId, Guid? unitOrganizationId, string? module,
        string? httpMethod, int page, int pageSize, CancellationToken cancellationToken);
}
