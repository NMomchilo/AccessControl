using Microsoft.EntityFrameworkCore;

namespace UserManagement.Infrastructure.Database;

public sealed class UserManagementDbContext(DbContextOptions<UserManagementDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<UnitOrganizationEntity> UnitOrganizations => Set<UnitOrganizationEntity>();
    public DbSet<UnitOrganizationMembershipEntity> UnitOrganizationMemberships => Set<UnitOrganizationMembershipEntity>();
    public DbSet<UserManagementRevisionEntity> Revisions => Set<UserManagementRevisionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(user => user.ObjectId).HasMaxLength(36).IsRequired();
            entity.HasIndex(user => new { user.TenantId, user.ObjectId }).IsUnique();
            entity.HasAlternateKey(user => new { user.TenantId, user.Id });
            entity.Property(user => user.Email).HasMaxLength(320);
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.CreatedAt).IsRequired();
            entity.HasMany(user => user.Organizations).WithOne(membership => membership.User)
                .HasForeignKey(membership => new { membership.TenantId, membership.UserId })
                .HasPrincipalKey(user => new { user.TenantId, user.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UnitOrganizationEntity>(entity =>
        {
            entity.ToTable("UnitOrganizations");
            entity.HasKey(organization => organization.Id);
            entity.HasAlternateKey(organization => new { organization.TenantId, organization.Id });
            entity.Property(organization => organization.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(organization => organization.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(organization => new { organization.TenantId, organization.Name }).IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<UnitOrganizationMembershipEntity>(entity =>
        {
            entity.ToTable("UnitOrganizationMemberships");
            entity.HasKey(membership => new { membership.UserId, membership.UnitOrganizationId });
            entity.HasAlternateKey(membership => new
                { membership.TenantId, membership.UserId, membership.UnitOrganizationId });
            entity.Property(membership => membership.TenantId).HasMaxLength(128).IsRequired();
            entity.HasOne(membership => membership.UnitOrganization)
                .WithMany(organization => organization.Members)
                .HasForeignKey(membership => new { membership.TenantId, membership.UnitOrganizationId })
                .HasPrincipalKey(organization => new { organization.TenantId, organization.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserRoleEntity>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(role => new { role.UserId, role.UnitOrganizationId, role.RoleId });
            entity.Property(role => role.TenantId).HasMaxLength(128).IsRequired();
            entity.HasOne(role => role.User).WithMany(user => user.Roles)
                .HasForeignKey(role => new { role.TenantId, role.UserId })
                .HasPrincipalKey(user => new { user.TenantId, user.Id })
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(role => role.Membership).WithMany(membership => membership.Roles)
                .HasForeignKey(role => new { role.TenantId, role.UserId, role.UnitOrganizationId })
                .HasPrincipalKey(membership => new
                    { membership.TenantId, membership.UserId, membership.UnitOrganizationId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(role => role.Role).WithMany(role => role.Users)
                .HasForeignKey(role => new { role.TenantId, role.UnitOrganizationId, role.RoleId })
                .HasPrincipalKey(role => new { role.TenantId, role.UnitOrganizationId, role.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleEntity>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(role => role.Id);
            entity.HasAlternateKey(role => new { role.TenantId, role.UnitOrganizationId, role.Id });
            entity.Property(role => role.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(role => role.Code).HasMaxLength(64).IsRequired();
            entity.Property(role => role.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(role => role.IsActive).IsRequired();
            entity.HasIndex(role => new { role.TenantId, role.UnitOrganizationId, role.Code }).IsUnique();
            entity.HasOne(role => role.UnitOrganization).WithMany(organization => organization.Roles)
                .HasForeignKey(role => new { role.TenantId, role.UnitOrganizationId })
                .HasPrincipalKey(organization => new { organization.TenantId, organization.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserManagementRevisionEntity>(entity =>
        {
            entity.ToTable("UserManagementRevisions");
            entity.HasKey(value => value.Id);
            entity.HasData(new UserManagementRevisionEntity { Id = 1, Version = 0 });
        });
    }
}
