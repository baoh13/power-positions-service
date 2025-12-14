
# Multi-stage Dockerfile for Power Position Tracker .NET 9 Worker Service

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY ["src/power-position-tracker/power-position-tracker.csproj", "power-position-tracker/"]
RUN dotnet restore "power-position-tracker/power-position-tracker.csproj"

# Copy source code
COPY src/power-position-tracker/ power-position-tracker/

# Build application
WORKDIR /src/power-position-tracker
RUN dotnet build "power-position-tracker.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "power-position-tracker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

# Create non-root user for security
RUN groupadd -g 1000 powerposition && \
    useradd -u 1000 -g powerposition -s /bin/bash -m powerposition

# Create directories for data volumes (will be mounted from Kubernetes)
RUN mkdir -p /app/data/output /app/data/audit /app/data/dlq && \
    chown -R powerposition:powerposition /app/data

# Copy published application
COPY --from=publish /app/publish .

# Copy PowerService.dll from docs directory
COPY src/power-position-tracker/docs/PowerService.dll .

# Switch to non-root user
USER powerposition

# Health check (requires health endpoint implementation)
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD dotnet /app/power-position-tracker.dll --health || exit 1

# Entry point
ENTRYPOINT ["dotnet", "power-position-tracker.dll"]
