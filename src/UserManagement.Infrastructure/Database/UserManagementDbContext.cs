using Microsoft.EntityFrameworkCore;

namespace UserManagement.Infrastructure.Database;

public sealed class UserManagementDbContext(DbContextOptions<UserManagementDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
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
            entity.HasMany(user => user.Roles).WithOne(role => role.User)
                .HasForeignKey(role => new { role.TenantId, role.UserId })
                .HasPrincipalKey(user => new { user.TenantId, user.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRoleEntity>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(role => new { role.UserId, role.RoleId });
            entity.Property(role => role.TenantId).HasMaxLength(128).IsRequired();
            entity.HasOne(role => role.Role).WithMany(role => role.Users)
                .HasForeignKey(role => new { role.TenantId, role.RoleId })
                .HasPrincipalKey(role => new { role.TenantId, role.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleEntity>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(role => role.Id);
            entity.HasAlternateKey(role => new { role.TenantId, role.Id });
            entity.Property(role => role.TenantId).HasMaxLength(128).IsRequired();
            entity.Property(role => role.Code).HasMaxLength(64).IsRequired();
            entity.Property(role => role.DisplayName).HasMaxLength(200).IsRequired();
            entity.HasIndex(role => new { role.TenantId, role.Code }).IsUnique();
        });

        modelBuilder.Entity<UserManagementRevisionEntity>(entity =>
        {
            entity.ToTable("UserManagementRevisions");
            entity.HasKey(value => value.Id);
            entity.HasData(new UserManagementRevisionEntity { Id = 1, Version = 0 });
        });
    }
}
