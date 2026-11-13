using Microsoft.EntityFrameworkCore;

namespace AccessControl.Infrastructure.Database;

public sealed class AccessControlDbContext(DbContextOptions<AccessControlDbContext> options) : DbContext(options)
{
    public DbSet<RoleEntitlementEntity> RoleEntitlements => Set<RoleEntitlementEntity>();
    public DbSet<EntitlementEntity> Entitlements => Set<EntitlementEntity>();
    public DbSet<EndpointPolicyEntity> EndpointPolicies => Set<EndpointPolicyEntity>();
    public DbSet<EndpointPolicyEntitlementEntity> EndpointPolicyEntitlements => Set<EndpointPolicyEntitlementEntity>();
    public DbSet<AuthorizationRevisionEntity> AuthorizationRevisions => Set<AuthorizationRevisionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntitlementEntity>(entity =>
        {
            entity.ToTable("Entitlements");
            entity.HasKey(value => value.Id);
            entity.HasAlternateKey(value => new { value.TenantId, value.UnitOrganizationId, value.Id });
            entity.Property(value => value.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(value => value.Code).HasMaxLength(128).IsRequired();
            entity.Property(value => value.DisplayName).HasMaxLength(200).IsRequired();
            entity.HasIndex(value => new { value.TenantId, value.UnitOrganizationId, value.Code }).IsUnique();
        });

        modelBuilder.Entity<RoleEntitlementEntity>(entity =>
        {
            entity.ToTable("RoleEntitlements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RoleId).IsRequired();
            entity.Property(x => x.UnitOrganizationId).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.UnitOrganizationId, x.RoleId, x.EntitlementId }).IsUnique();
            entity.HasOne(x => x.Entitlement).WithMany(x => x.RoleGrants)
                .HasForeignKey(x => new { x.TenantId, x.UnitOrganizationId, x.EntitlementId })
                .HasPrincipalKey(x => new { x.TenantId, x.UnitOrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EndpointPolicyEntity>(entity =>
        {
            entity.ToTable("EndpointPolicies");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.TenantId, x.UnitOrganizationId, x.Id });
            entity.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.UnitOrganizationId).IsRequired();
            entity.Property(x => x.Module).HasMaxLength(128).IsRequired();
            entity.Property(x => x.HttpMethod).HasMaxLength(16).IsRequired();
            entity.Property(x => x.RouteTemplate).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.UnitOrganizationId, x.Module, x.HttpMethod, x.RouteTemplate }).IsUnique();
            entity.HasMany(x => x.RequiredEntitlements).WithOne(x => x.EndpointPolicy)
                .HasForeignKey(x => new { x.TenantId, x.UnitOrganizationId, x.EndpointPolicyId })
                .HasPrincipalKey(x => new { x.TenantId, x.UnitOrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EndpointPolicyEntitlementEntity>(entity =>
        {
            entity.ToTable("EndpointPolicyEntitlements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.EndpointPolicyId, x.EntitlementId }).IsUnique();
            entity.HasOne(x => x.Entitlement).WithMany(x => x.PolicyRequirements)
                .HasForeignKey(x => new { x.TenantId, x.UnitOrganizationId, x.EntitlementId })
                .HasPrincipalKey(x => new { x.TenantId, x.UnitOrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuthorizationRevisionEntity>(entity =>
        {
            entity.ToTable("AuthorizationRevisions");
            entity.HasKey(x => x.Id);
            entity.HasData(new AuthorizationRevisionEntity { Id = 1, Version = 0 });
        });
    }
}
