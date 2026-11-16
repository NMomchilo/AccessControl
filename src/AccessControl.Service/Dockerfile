# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NuGet.config", "."]
COPY ["src/AccessControl.Service/AccessControl.Service.csproj", "src/AccessControl.Service/"]
COPY ["src/AccessControl.Application/AccessControl.Application.csproj", "src/AccessControl.Application/"]
COPY ["src/AccessControl.Domain/AccessControl.Domain.csproj", "src/AccessControl.Domain/"]
COPY ["src/AccessControl.Contracts/AccessControl.Contracts.csproj", "src/AccessControl.Contracts/"]
COPY ["src/AccessControl.Infrastructure/AccessControl.Infrastructure.csproj", "src/AccessControl.Infrastructure/"]
RUN dotnet restore "./src/AccessControl.Service/AccessControl.Service.csproj"
COPY . .
WORKDIR "/src/src/AccessControl.Service"
RUN dotnet build "./AccessControl.Service.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AccessControl.Service.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AccessControl.Service.dll"]