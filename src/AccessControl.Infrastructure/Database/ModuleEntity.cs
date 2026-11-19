using Common.Persistence;

namespace AccessControl.Infrastructure.Database;

public sealed class ModuleEntity : GeneralEntityBase
{
    public string TenantId { get; set; } = string.Empty;
    public Guid UnitOrganizationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
