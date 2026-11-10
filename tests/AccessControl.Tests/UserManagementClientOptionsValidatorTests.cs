using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UserManagement.Client;

namespace AccessControl.Tests;

public sealed class UserManagementClientOptionsValidatorTests
{
    [Fact]
    public void Validate_HttpInProduction_FailsEvenWhenOptedIn()
    {
        UserManagementClientOptionsValidator sut = new(new TestEnvironment(Environments.Production));

        Microsoft.Extensions.Options.ValidateOptionsResult result = sut.Validate(null,
            new UserManagementClientOptions
            {
                ServiceUrl = "http://localhost:5220/",
                AllowInsecureHttpForDevelopment = true
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_HttpInDevelopmentWithOptIn_Succeeds()
    {
        UserManagementClientOptionsValidator sut = new(new TestEnvironment(Environments.Development));

        Microsoft.Extensions.Options.ValidateOptionsResult result = sut.Validate(null,
            new UserManagementClientOptions
            {
                ServiceUrl = "http://localhost:5220/",
                AllowInsecureHttpForDevelopment = true
            });

        Assert.True(result.Succeeded);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
