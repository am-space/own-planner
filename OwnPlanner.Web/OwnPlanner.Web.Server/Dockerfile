# Multi-stage Dockerfile for OwnPlanner
# Builds and runs the Web.Server container
# Build context: Repository root directory

# Stage 1: Setup Node.js for React frontend
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS with-node
WORKDIR /src

# Install Node.js 24.x (matches .node-version and CI)
RUN apt-get update && \
    apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_24.x | bash - && \
    apt-get install -y nodejs && \
    rm -rf /var/lib/apt/lists/*

# Stage 2: Build Web.Server with React frontend
FROM with-node AS build
ARG BUILD_CONFIGURATION=Release
ARG APP_VERSION=0.0.0-local
ARG APP_FILE_VERSION=0.0.0.0
ENV APP_VERSION=$APP_VERSION \
    APP_FILE_VERSION=$APP_FILE_VERSION \
    VITE_APP_VERSION=$APP_VERSION
WORKDIR /src

# Copy Web.Server project and dependencies
COPY ["Directory.Build.props", "."]
COPY ["OwnPlanner.Web/OwnPlanner.Web.Server/OwnPlanner.Web.Server.csproj", "OwnPlanner.Web/OwnPlanner.Web.Server/"]
COPY ["OwnPlanner.Web/ownplanner.web.client/ownplanner.web.client.esproj", "OwnPlanner.Web/ownplanner.web.client/"]
COPY ["OwnPlanner.Mcp.Tools/OwnPlanner.Mcp.Tools.csproj", "OwnPlanner.Mcp.Tools/"]
COPY ["OwnPlanner.Application/OwnPlanner.Application.csproj", "OwnPlanner.Application/"]
COPY ["OwnPlanner.Domain/OwnPlanner.Domain.csproj", "OwnPlanner.Domain/"]
COPY ["OwnPlanner.Infrastructure/OwnPlanner.Infrastructure.csproj", "OwnPlanner.Infrastructure/"]

RUN dotnet restore "OwnPlanner.Web/OwnPlanner.Web.Server/OwnPlanner.Web.Server.csproj"

# Copy all source code
COPY . .

# Build and publish Web.Server (includes React frontend build via npm)
WORKDIR "/src/OwnPlanner.Web/OwnPlanner.Web.Server"
RUN dotnet build "OwnPlanner.Web.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "OwnPlanner.Web.Server.csproj" -c $BUILD_CONFIGURATION -o /app/web /p:UseAppHost=false --no-restore

# Stage 3: Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG APP_VERSION=0.0.0-local
LABEL org.opencontainers.image.version=$APP_VERSION
WORKDIR /app

# Install runtime dependencies (curl for health checks)
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    curl \
    sqlite3 \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Copy published Web.Server from build stage
COPY --from=publish /app/web ./

# Create data and log directories and set ownership to APP_UID user
RUN mkdir -p /app/data/auth /app/data/databases /app/logs && \
    chown -R $APP_UID:$APP_UID /app/data /app/logs

# Set environment variables
# Core ASP.NET settings
ENV ASPNETCORE_URLS=http://+:8080 \
    APP_VERSION=$APP_VERSION \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

# Application configuration (paths match created directories)
ENV Database__AuthDbPath=/app/data/auth/auth.db \
    Database__UserDbDirectory=/app/data/databases

# Expose port (only 8080 for production)
EXPOSE 8080

# Health check configuration
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/api/chat/health || exit 1

# Switch to non-root user for security (using base image's APP_UID)
USER $APP_UID

# Entry point
ENTRYPOINT ["dotnet", "OwnPlanner.Web.Server.dll"]
