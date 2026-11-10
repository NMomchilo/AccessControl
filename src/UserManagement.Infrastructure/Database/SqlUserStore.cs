using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UserManagement.Application;
using UserManagement.Domain;

namespace UserManagement.Infrastructure.Database;

public sealed class SqlUserStore(UserManagementDbContext db, ILogger<SqlUserStore> logger) : IUserStore
{
    public async Task<ManagedUser> ResolveAsync(string objectId, string? email, string? displayName,
        CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.Include(value => value.Roles)
            .SingleOrDefaultAsync(value => value.ObjectId == objectId, cancellationToken);
        if (user is null)
        {
            user = new UserEntity
            {
                Id = Guid.NewGuid(), ObjectId = objectId, Email = email,
                DisplayName = displayName ?? email ?? "Unknown user", CreatedAt = DateTimeOffset.UtcNow,
                Roles = [new UserRoleEntity { Role = UserRoles.Reviewer }]
            };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Provisioned user {UserId} with Reviewer role", user.Id);
                return Map(user);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                user = await db.Users.Include(value => value.Roles)
                    .SingleOrDefaultAsync(value => value.ObjectId == objectId, cancellationToken);
                if (user is null)
                {
                    throw;
                }

                logger.LogInformation("Concurrent user provisioning resolved for {UserId}", user.Id);
                return Map(user);
            }
        }

        if (email is not null && user.Email != email)
        {
            user.Email = email;
        }

        if (displayName is not null && user.DisplayName != displayName)
        {
            user.DisplayName = displayName;
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogDebug("Updated profile for user {UserId}", user.Id);
        }

        return Map(user);
    }

    public async Task<ManagedUser?> GetAsync(string objectId, CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.AsNoTracking().Include(value => value.Roles)
            .SingleOrDefaultAsync(value => value.ObjectId == objectId, cancellationToken);
        return user is null ? null : Map(user);
    }

    public async Task<bool> ReplaceRolesAsync(string objectId, IReadOnlyList<string> roles, CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.Include(value => value.Roles)
            .SingleOrDefaultAsync(value => value.ObjectId == objectId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        db.UserRoles.RemoveRange(user.Roles);
        user.Roles = roles.Select(role => new UserRoleEntity { UserId = user.Id, Role = role }).ToList();
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Replaced roles for user {UserId}; count {RoleCount}", user.Id, roles.Count);
        return true;
    }

    public async Task<bool> SetActiveAsync(string objectId, bool isActive, CancellationToken cancellationToken)
    {
        UserEntity? user = await db.Users.SingleOrDefaultAsync(value => value.ObjectId == objectId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);
        user.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        await IncrementRevisionAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Changed active status for user {UserId} to {IsActive}", user.Id, isActive);
        return true;
    }

    public Task<long> GetRevisionAsync(CancellationToken cancellationToken) =>
        db.Revisions.AsNoTracking().Where(value => value.Id == 1)
            .Select(value => value.Version).SingleAsync(cancellationToken);

    private async Task IncrementRevisionAsync(CancellationToken cancellationToken)
    {
        int affected = await db.Revisions.Where(value => value.Id == 1)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Version,
                value => value.Version + 1), cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("User Management revision row is missing.");
        }
    }

    private static ManagedUser Map(UserEntity user) => new(user.Id, user.ObjectId, user.Email,
        user.DisplayName, user.IsActive, user.CreatedAt,
        user.Roles.Select(value => value.Role).Order(StringComparer.Ordinal).ToArray());
}
