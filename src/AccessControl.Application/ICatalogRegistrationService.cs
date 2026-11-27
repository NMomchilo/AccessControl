using AccessControl.Contracts;

namespace AccessControl.Application;

public interface ICatalogRegistrationService
{
    Task<CatalogRegistrationResult> RegisterAsync(CatalogRegistrationRequest request,
        CancellationToken cancellationToken);
}

public sealed class CatalogRegistrationConflictException : Exception;
