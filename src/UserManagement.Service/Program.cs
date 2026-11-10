using Microsoft.EntityFrameworkCore;
using UserManagement.Application;
using UserManagement.Infrastructure;
using UserManagement.Infrastructure.Database;
using UserManagement.Service.Security;

namespace UserManagement.Service;

public static class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddUserManagementApplication();
        builder.Services.AddUserManagementInfrastructure(builder.Configuration);
        builder.Services.AddOptions<UserManagementOptions>()
            .Bind(builder.Configuration.GetSection(UserManagementOptions.SectionName))
            .Validate(UserManagementOptions.IsValid, "UserManagementRules exceed database limits.")
            .ValidateOnStart();
        builder.Services.AddOptions<UserManagementKeyOptions>()
            .Bind(builder.Configuration.GetSection(UserManagementKeyOptions.SectionName))
            .Validate(UserManagementKeyOptions.IsValid,
                "Configure distinct client and administrator credentials of at least 32 characters.")
            .ValidateOnStart();
        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<UserManagementExceptionHandler>();

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        else
        {
            app.UseStaticFiles();
            app.UseSwaggerUI(options =>
            {
                options.RoutePrefix = "swagger";
                options.SwaggerEndpoint("/openapi/user-management.v1.json", "User Management API v1");
            });
        }
        app.UseMiddleware<UserManagementKeyMiddleware>();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/ready", async (UserManagementDbContext db, CancellationToken cancellationToken) =>
            await db.Database.CanConnectAsync(cancellationToken)
                ? Results.Ok(new { status = "ready" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        app.MapControllers();
        if (app.Environment.IsDevelopment() &&
            app.Configuration.GetValue<bool>("UserManagementDatabase:ApplyMigrationsOnStartup"))
        {
            using IServiceScope scope = app.Services.CreateScope();
            UserManagementDbContext db = scope.ServiceProvider.GetRequiredService<UserManagementDbContext>();
            app.Logger.LogInformation("Applying User Management database migrations for local Development");
            db.Database.Migrate();
            app.Logger.LogInformation("User Management database migrations are current");
        }

        app.Run();
    }
}

