# Multi-stage Dockerfile for OwnPlanner
# Combines Web.Server + MCP.StdioApp in a single container
# Build context: Repository root directory

# Stage 1: Build MCP.StdioApp
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-mcp
WORKDIR /src

# Copy MCP project and dependencies
COPY ["OwnPlanner.Mcp.StdioApp/OwnPlanner.Mcp.StdioApp.csproj", "OwnPlanner.Mcp.StdioApp/"]
COPY ["OwnPlanner.Application/OwnPlanner.Application.csproj", "OwnPlanner.Application/"]
COPY ["OwnPlanner.Domain/OwnPlanner.Domain.csproj", "OwnPlanner.Domain/"]
COPY ["OwnPlanner.Infrastructure/OwnPlanner.Infrastructure.csproj", "OwnPlanner.Infrastructure/"]

RUN dotnet restore "OwnPlanner.Mcp.StdioApp/OwnPlanner.Mcp.StdioApp.csproj"

# Copy all source code
COPY . .

# Build and publish MCP.StdioApp
WORKDIR "/src/OwnPlanner.Mcp.StdioApp"
RUN dotnet publish "OwnPlanner.Mcp.StdioApp.csproj" -c Release -o /app/mcp --no-restore

# Stage 2: Setup Node.js for React frontend
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS with-node
WORKDIR /src

# Install Node.js 20.x (optimized commands)
RUN apt-get update && \
    apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    rm -rf /var/lib/apt/lists/*

# Stage 3: Build Web.Server with React frontend
FROM with-node AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy Web.Server project and dependencies
COPY ["OwnPlanner.Web/OwnPlanner.Web.Server/OwnPlanner.Web.Server.csproj", "OwnPlanner.Web/OwnPlanner.Web.Server/"]
COPY ["OwnPlanner.Web/ownplanner.web.client/ownplanner.web.client.esproj", "OwnPlanner.Web/ownplanner.web.client/"]
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

# Stage 4: Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Install runtime dependencies (curl for health checks)
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Copy published Web.Server from build stage
COPY --from=publish /app/web ./

# Copy published MCP.StdioApp to mcp subdirectory
COPY --from=build-mcp /app/mcp ./mcp/

# Create data directories and set ownership to APP_UID user
RUN mkdir -p /app/data/auth /app/data/databases /app/data/logs && \
    chown -R $APP_UID:$APP_UID /app/data

# Set environment variables
# Core ASP.NET settings
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

# Application configuration (paths match created directories)
ENV Database__AuthDbPath=/app/data/auth/auth.db \
    Chat__Mcp__Command=dotnet \
    Chat__Mcp__Arguments__0=/app/mcp/OwnPlanner.Mcp.StdioApp.dll

# MCP.StdioApp configuration (used by spawned MCP processes)
ENV MCP_DATA_DIR=/app/data/databases \
    MCP_LOG_DIR=/app/data/logs

# Expose port (only 8080 for production)
EXPOSE 8080

# Health check configuration
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/api/chat/health || exit 1

# Switch to non-root user for security (using base image's APP_UID)
USER $APP_UID

# Entry point
ENTRYPOINT ["dotnet", "OwnPlanner.Web.Server.dll"]
