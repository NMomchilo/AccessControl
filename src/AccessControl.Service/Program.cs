using AccessControl.Application;
using AccessControl.Infrastructure;
using AccessControl.Service.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOptions<ApiKeyOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.DecisionKey) &&
        !string.IsNullOrWhiteSpace(options.AdminKey) &&
        options.DecisionKey != options.AdminKey,
        "Distinct decision and admin API keys are required.")
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
app.UseMiddleware<ApiKeyMiddleware>();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.Run();

public partial class Program;
