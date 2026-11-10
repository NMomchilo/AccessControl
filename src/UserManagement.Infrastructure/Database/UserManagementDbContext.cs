using Microsoft.EntityFrameworkCore;

namespace UserManagement.Infrastructure.Database;

public sealed class UserManagementDbContext(DbContextOptions<UserManagementDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<UserRoleEntity> UserRoles => Set<UserRoleEntity>();
    public DbSet<UserManagementRevisionEntity> Revisions => Set<UserManagementRevisionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.ObjectId).HasMaxLength(36).IsRequired();
            entity.HasIndex(user => user.ObjectId).IsUnique();
            entity.Property(user => user.Email).HasMaxLength(320);
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.CreatedAt).IsRequired();
            entity.HasMany(user => user.Roles).WithOne(role => role.User)
                .HasForeignKey(role => role.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRoleEntity>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(role => new { role.UserId, role.Role });
            entity.Property(role => role.Role).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<UserManagementRevisionEntity>(entity =>
        {
            entity.ToTable("UserManagementRevisions");
            entity.HasKey(value => value.Id);
            entity.HasData(new UserManagementRevisionEntity { Id = 1, Version = 0 });
        });
    }
}
