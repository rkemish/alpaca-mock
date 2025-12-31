# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY AlpacaMock.sln .
COPY src/AlpacaMock.Api/AlpacaMock.Api.csproj src/AlpacaMock.Api/
COPY src/AlpacaMock.Domain/AlpacaMock.Domain.csproj src/AlpacaMock.Domain/
COPY src/AlpacaMock.Infrastructure/AlpacaMock.Infrastructure.csproj src/AlpacaMock.Infrastructure/

# Restore dependencies
RUN dotnet restore src/AlpacaMock.Api/AlpacaMock.Api.csproj

# Copy source code
COPY src/ src/

# Build and publish
RUN dotnet publish src/AlpacaMock.Api/AlpacaMock.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Expose port
EXPOSE 8080

# Set environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the app
ENTRYPOINT ["dotnet", "AlpacaMock.Api.dll"]
