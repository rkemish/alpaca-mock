# Build stage - .NET 10.0 GA (LTS) on Ubuntu Noble
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for restore layer caching
COPY src/AlpacaMock.Api/AlpacaMock.Api.csproj src/AlpacaMock.Api/
COPY src/AlpacaMock.Domain/AlpacaMock.Domain.csproj src/AlpacaMock.Domain/
COPY src/AlpacaMock.Infrastructure/AlpacaMock.Infrastructure.csproj src/AlpacaMock.Infrastructure/

# Restore dependencies for API and its dependencies
RUN dotnet restore src/AlpacaMock.Api/AlpacaMock.Api.csproj

# Copy source code
COPY src/AlpacaMock.Api/ src/AlpacaMock.Api/
COPY src/AlpacaMock.Domain/ src/AlpacaMock.Domain/
COPY src/AlpacaMock.Infrastructure/ src/AlpacaMock.Infrastructure/

# Build and publish (restore is cached from previous step)
RUN dotnet publish src/AlpacaMock.Api/AlpacaMock.Api.csproj \
    -c Release \
    -o /app/publish

# Runtime stage - regular Ubuntu image for proper DNS resolution in Docker
# Note: Chiseled images have DNS issues in Docker Compose networks
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Chiseled images run as non-root by default (no USER directive needed)

# Copy published app
COPY --from=build /app/publish .

# Expose port
EXPOSE 8080

# Set environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Note: No HEALTHCHECK here - Container Apps uses its own liveness/readiness probes
# configured in the Bicep template at /health endpoint

# Run the app
ENTRYPOINT ["dotnet", "AlpacaMock.Api.dll"]
