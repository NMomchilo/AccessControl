using AccessControl.Contracts;
using AccessControl.Application;
using AccessControl.Service.Security;
using Microsoft.AspNetCore.Mvc;

namespace AccessControl.Service.Controllers;

[ApiController]
[Route("v1/registrations/catalog")]
public sealed class CatalogRegistrationController(ICatalogRegistrationService store) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> RegisterAsync([FromBody] CatalogRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items[nameof(DecisionClientCredential)] is not DecisionClientCredential client ||
            !client.CanRegisterCatalog)
            return Forbid();
        if (request is null || string.IsNullOrWhiteSpace(request.TenantId) || request.TenantId.Length > 128 ||
            request.UnitOrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Module) ||
            request.Module.Length > 128 || string.IsNullOrWhiteSpace(request.ModuleName) ||
            request.ModuleName.Length > 200 || request.Endpoints is null || request.Endpoints.Count > 1000 ||
            request.Endpoints.Any(endpoint => endpoint is null ||
                string.IsNullOrWhiteSpace(endpoint.HttpMethod) || endpoint.HttpMethod.Length > 16 ||
                string.IsNullOrWhiteSpace(endpoint.RouteTemplate) || endpoint.RouteTemplate.Length > 512))
            return BadRequest();

        string tenant = request.TenantId.Trim();
        string module = request.Module.Trim().ToLowerInvariant();
        CatalogRegistrationRequest normalized = request with
        {
            TenantId = tenant, Module = module, ModuleName = request.ModuleName.Trim(),
            Endpoints = request.Endpoints.Select(endpoint => new CatalogEndpoint(
                EndpointKey.NormalizeMethod(endpoint.HttpMethod),
                EndpointKey.NormalizeRoute(endpoint.RouteTemplate))).Distinct().ToArray()
        };
        if (normalized.Endpoints.Any(endpoint => endpoint.HttpMethod.Length > 16 ||
                endpoint.RouteTemplate.Length > 512)) return BadRequest();
        try
        {
            return Ok(await store.RegisterAsync(normalized, cancellationToken));
        }
        catch (CatalogRegistrationConflictException)
        {
            return Conflict();
        }
    }
}
