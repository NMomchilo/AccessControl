using Microsoft.EntityFrameworkCore;

namespace AccessControl.Infrastructure.Database;

public sealed class AccessControlDbContext(DbContextOptions<AccessControlDbContext> options) : DbContext(options)
{
    public DbSet<PolicyEntity> Policies => Set<PolicyEntity>();
    public DbSet<RoleEntitlementEntity> RoleEntitlements => Set<RoleEntitlementEntity>();
    public DbSet<EndpointPolicyEntity> EndpointPolicies => Set<EndpointPolicyEntity>();
    public DbSet<EndpointPolicyEntitlementEntity> EndpointPolicyEntitlements => Set<EndpointPolicyEntitlementEntity>();
    public DbSet<AuthorizationRevisionEntity> AuthorizationRevisions => Set<AuthorizationRevisionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PolicyEntity>(entity =>
        {
            entity.ToTable("AccessPolicies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Module).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Resource).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Entitlement).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Module, x.Resource, x.Action, x.Entitlement }).IsUnique();
        });

        modelBuilder.Entity<RoleEntitlementEntity>(entity =>
        {
            entity.ToTable("RoleEntitlements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RoleId).IsRequired();
            entity.Property(x => x.Entitlement).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.RoleId, x.Entitlement }).IsUnique();
        });

        modelBuilder.Entity<EndpointPolicyEntity>(entity =>
        {
            entity.ToTable("EndpointPolicies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Module).HasMaxLength(128).IsRequired();
            entity.Property(x => x.HttpMethod).HasMaxLength(16).IsRequired();
            entity.Property(x => x.RouteTemplate).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Module, x.HttpMethod, x.RouteTemplate }).IsUnique();
            entity.HasMany(x => x.RequiredEntitlements).WithOne(x => x.EndpointPolicy)
                .HasForeignKey(x => x.EndpointPolicyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EndpointPolicyEntitlementEntity>(entity =>
        {
            entity.ToTable("EndpointPolicyEntitlements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Entitlement).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.EndpointPolicyId, x.Entitlement }).IsUnique();
        });

        modelBuilder.Entity<AuthorizationRevisionEntity>(entity =>
        {
            entity.ToTable("AuthorizationRevisions");
            entity.HasKey(x => x.Id);
            entity.HasData(new AuthorizationRevisionEntity { Id = 1, Version = 0 });
        });
    }
}
