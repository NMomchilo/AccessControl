using AdminManagement.Security;
using AccessControl.Application;
using AccessControl.Infrastructure;
using AccessControl.Infrastructure.Database;
using AccessControl.Service.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEntraAdminAuthentication(builder.Configuration);
builder.Services.AddOptions<DecisionEntraOptions>()
    .Bind(builder.Configuration.GetSection(DecisionEntraOptions.SectionName))
    .Validate(DecisionEntraOptions.IsValid, "DecisionEntra requires an explicit HTTPS authority and audience.")
    .ValidateOnStart();
DecisionEntraOptions decisionEntra = builder.Configuration.GetSection(DecisionEntraOptions.SectionName)
    .Get<DecisionEntraOptions>() ?? new();
builder.Services.AddAuthentication().AddJwtBearer("DecisionEntra", options =>
{
    options.Authority = decisionEntra.Authority;
    options.Audience = decisionEntra.Audience;
    options.MapInboundClaims = false;
    options.RequireHttpsMetadata = true;
});
builder.Services.AddScoped<IAdminDutyReader, AccessControlAdminDutyReader>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddOptions<UserManagementAdminLookupOptions>()
    .Bind(builder.Configuration.GetSection(UserManagementAdminLookupOptions.SectionName))
    .Validate(UserManagementAdminLookupOptions.IsValid,
        "UserManagementAdminLookup needs an absolute service URL and a 32-character lookup key.")
    .Validate(value => builder.Environment.IsDevelopment() ||
        Uri.TryCreate(value.ServiceUrl, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps,
        "UserManagementAdminLookup must use HTTPS outside Development.")
    .ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IAdminScopeAuthorizer, UserManagementAdminScopeAuthorizer>((provider, client) =>
{
    UserManagementAdminLookupOptions lookup = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<UserManagementAdminLookupOptions>>().Value;
    client.BaseAddress = new Uri(lookup.ServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddOptions<ApiKeyOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName))
    .Validate(ApiKeyOptions.IsValid,
        "Configure distinct scoped decision clients and administrator credentials.")
    .ValidateOnStart();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.UseHttpsRedirection();
if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/access-control.v1.json", "Access Control API v1");
    });
}
app.UseAuthentication();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
if (app.Environment.IsDevelopment() &&
    app.Configuration.GetValue<bool>("AccessControlDatabase:ApplyMigrationsOnStartup"))
{
    using IServiceScope scope = app.Services.CreateScope();
    AccessControlDbContext db = scope.ServiceProvider.GetRequiredService<AccessControlDbContext>();
    app.Logger.LogInformation("Applying Access Control database migrations for local Development");
    db.Database.Migrate();
    app.Logger.LogInformation("Access Control database migrations are current");
}

app.Run();

public partial class Program;
