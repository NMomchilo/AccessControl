using AccessControl.Contracts;

namespace AccessControl.Application;

public interface ICatalogRegistrationService
{
    Task<CatalogRegistrationResult> RegisterAsync(CatalogRegistrationRequest request, string clientId,
        CancellationToken cancellationToken);
}

public sealed class CatalogRegistrationConflictException : Exception;
