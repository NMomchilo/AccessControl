using AccessControl.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using UserManagement.Client;
using Shared.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddSharedKeyVault();
AccessControlCatalogRegistrationOptions catalogRegistration = builder.Configuration
    .GetRequiredSection("AccessControl:Registration")
    .Get<AccessControlCatalogRegistrationOptions>()
    ?? throw new InvalidOperationException("AccessControl:Registration is required.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddUserManagement(builder.Configuration);
builder.Services.AddAccessControl(builder.Configuration);
builder.Services.AddControllers();

WebApplication app = builder.Build();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseUserManagement();
app.UseAccessControl();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
await app.RegisterAccessControlCatalogAsync(catalogRegistration);
app.Run();
