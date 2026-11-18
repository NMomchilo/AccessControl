using Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace UserManagement.Infrastructure.Database;

public sealed class UserManagementDbContext(DbContextOptions<UserManagementDbContext> options,
    ICurrentActorProvider? currentActor = null) : DbContext(options)
{
    internal string RequiredActorId => CurrentActor.Require(currentActor);

    public override int SaveChanges()
    {
        StampChanges();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampChanges()
    {
        DateTime now = DateTime.UtcNow;
        string actor = RequiredActorId;
        foreach (var entry in ChangeTracker.Entries<GeneralEntityBase>()
                     .Where(value => value.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added && entry.Entity.Id == Guid.Empty)
                entry.Entity.Id = Guid.NewGuid();
            entry.Entity.LastUpdateUser = actor;
            entry.Entity.LastUpdateTime = now;
        }
    }

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
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320)
                .HasComputedColumnSql("LOWER([Email])", stored: true);
            entity.HasIndex(user => new { user.TenantId, user.NormalizedEmail });
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
            entity.HasKey(membership => membership.Id);
            entity.HasIndex(membership => new { membership.UserId, membership.UnitOrganizationId }).IsUnique();
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
            entity.HasKey(role => role.Id);
            entity.HasIndex(role => new { role.UserId, role.UnitOrganizationId, role.RoleId }).IsUnique();
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
            entity.HasData(new UserManagementRevisionEntity
            {
                Id = UserManagementRevisionEntity.SingletonId, Version = 0,
                LastUpdateUser = "Migration", LastUpdateTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        if (Database.IsSqlServer())
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                         .Where(value => typeof(GeneralEntityBase).IsAssignableFrom(value.ClrType)))
                modelBuilder.Entity(entityType.ClrType).Property<Guid>(nameof(GeneralEntityBase.Id))
                    .HasDefaultValueSql("NEWSEQUENTIALID()");
        }
    }
}
