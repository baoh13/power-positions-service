# Power Position Tracker

**Date:** December 14, 2025

A .NET 9 Windows Service that extracts power trading position data at regular intervals, aggregates it by hour, and generates CSV reports with comprehensive audit trails.

## Overview

The Power Position Tracker service:
- **Extracts** position data every 5 minutes (configurable) from PowerService API
- **Aggregates** trading positions by hour using London Local Time (Europe/London)
- **Generates** progressive snapshot CSV files throughout the trading day
- **Maintains** comprehensive audit logs and dead letter queue for failed extractions
- **Handles** BST/GMT timezone transitions accurately using NodaTime

### Intra-Day Snapshot Behavior

The service creates **progressive snapshots** as traders book new positions. For example, on December 10, 2025:
- **01:05** extraction → `PowerPosition_20251210_0105.csv` (early morning positions)
- **06:05** extraction → `PowerPosition_20251210_0605.csv` (more complete data)
- **14:05** extraction → `PowerPosition_20251210_1405.csv` (near-complete positions)

Each snapshot queries `PowerService.GetTradesAsync()` with the same date but receives increasingly complete data as the trading day progresses.

## Prerequisites

- **.NET 9 SDK** or later
- **Windows OS** (designed for Windows Service deployment, but can run on other platforms for development)
- **PowerService.dll** (located in `src/power-position-tracker/docs/`)
- Write permissions for output directories

## Quick Start

### 1. Clone and Build

```powershell
# Navigate to solution directory
cd c:\ai\labs\power-position-tracker\src\power-position-tracker

# Restore dependencies and build
dotnet build
```

### 2. Run Locally

```powershell
# Run with default configuration
dotnet run

# Check output in 
# power-position-tracker/docs/Output
# power-position-tracker/docs/Audit
# power-position-tracker/docs/Dlq

# Or use the startup script (recommended)
..\..\scripts\local-startup.ps1

# Useful commands to check app status
# View logs
kubectl logs -l app.kubernetes.io/name=power-position-service -n default --tail=100

# View file outputs
minikube ssh --profile=power-position-dev "ls -la /mnt/power-reports-dev/audit/"

minikube ssh --profile=power-position-dev "ls -la /mnt/power-reports-dev/output/"

minikube ssh --profile=power-position-dev "ls -la /mnt/power-reports-dev/dlq/"

# View file contents
minikube ssh --profile=power-position-dev "cat /mnt/power-reports-dev/audit/ExecutionAudit_20251214.csv"

minikube ssh --profile=power-position-dev "cat /mnt/power-reports-dev/output/PowerPosition_20251210_1405.csv"

minikube ssh --profile=power-position-dev "cat /mnt/power-reports-dev/dlq/FailedExtraction_20251210_1405.json"

```

### 3. Run Tests

```powershell
# Run unit tests
cd ..\power-position-tracker-tests
dotnet test

# Run integration tests
cd ..\power-position-tracker-integration-tests
dotnet test

# Run all tests from solution root
cd ..\power-position-tracker
dotnet test
```

## Local Development with `scripts/local-startup.ps1`

The `local-startup.ps1` script provides a streamlined development experience with:

### Features
- **Directory Setup**: Automatically creates required output directories if missing
- **Permission Validation**: Checks write permissions before starting
- **Configuration Validation**: Validates `appsettings.json` settings
- **Clean Logs**: Optional cleanup of old output/audit files
- **Environment Override**: Supports runtime override via `DOTNET_RUNTIME` environment variable

### Usage

```powershell
# Basic usage - starts service with validation
.\scripts\local-startup.ps1

# With cleanup of previous runs
.\scripts\local-startup.ps1 -CleanLogs

# With specific runtime override (for testing/debugging)
.\scripts\local-startup.ps1 -RuntimeOverride "2025-12-10T14:30:00Z"

# Validate configuration only (no service start)
.\scripts\local-startup.ps1 -ValidateOnly
```

### Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `-CleanLogs` | Deletes all files in Output, Audit, and DLQ directories before starting | `.\local-startup.ps1 -CleanLogs` |
| `-RuntimeOverride` | Sets `DOTNET_RUNTIME` environment variable for testing specific extraction times | `.\local-startup.ps1 -RuntimeOverride "2025-12-10T14:30:00Z"` |
| `-ValidateOnly` | Validates configuration and permissions without running the service | `.\local-startup.ps1 -ValidateOnly` |

### What It Does

1. **Validates Environment**
   - Checks .NET SDK installation
   - Verifies PowerService.dll exists
   - Validates `appsettings.json` syntax

2. **Prepares Directories**
   - Creates Output, Audit, and DLQ directories
   - Validates write permissions
   - Optionally cleans old files

3. **Sets Runtime Context**
   - Applies runtime override if specified
   - Displays effective configuration

4. **Starts Service**
   - Runs `dotnet run` from correct directory
   - Displays real-time logs

## Configuration

### Primary Configuration (`appsettings.json`)

```json
{
  "PowerPositionSettings": {
    "IntervalMinutes": 5,              // Extraction frequency
    "OutputDirectory": "C:\\PowerReports\\Output",
    "AuditDirectory": "C:\\PowerReports\\Audit",
    "DlqDirectory": "C:\\PowerReports\\Dlq",
    "TimeZoneId": "Europe/London",      // BST/GMT aware
    "RunTime": null,                    // null = current time
    "RetryAttempts": 3,
    "RetryDelaySeconds": 20,
    "MaxDlqRetryAttempts": 9
  },
  "Serilog": {
    "MinimumLevel": "Information"
  }
}
```

### Runtime Override Priority

Configuration resolution follows strict hierarchy:

1. **Environment Variable** (highest) - `DOTNET_RUNTIME`
2. **Configuration Setting** - `PowerPositionSettings.RunTime`
3. **Default** (lowest) - `DateTime.UtcNow`

```powershell
# Override runtime for testing
$env:DOTNET_RUNTIME = "2025-12-10T14:30:00Z"
dotnet run

# Clear override after testing
$env:DOTNET_RUNTIME = $null
```

## Output Files

### Business Reports (`OutputDirectory`)

**Format**: `PowerPosition_YYYYMMDD_HHMM.csv`

```csv
LocalTime,Volume
23:00,1250.5
00:00,890.2
01:00,1100.0
...
```

- **Timestamp in filename**: London Local Time at extraction
- **Content**: Hourly aggregated positions (24 rows for standard days)
- **Special handling**: 23 or 25 rows on BST transition days

### Audit Logs (`AuditDirectory`)

**Format**: `ExecutionAudit_YYYYMM.csv`

```csv
ExtractionTime,TargetDate,Status,RetryCount,ErrorMessage,FilePath
2025-12-10T14:05:00Z,2025-12-10,Success,0,,C:\PowerReports\Output\PowerPosition_20251210_1405.csv
2025-12-10T14:10:00Z,2025-12-10,Failed,3,Connection timeout,
```

- **Monthly rotation**: New file each month
- **UTC timestamps**: All times in UTC
- **Thread-safe**: Concurrent write operations supported

### Dead Letter Queue (`DlqDirectory`)

**Format**: `FailedExtraction_YYYYMMDD_HHMM.json`

```json
{
  "TargetDate": "2025-12-10T00:00:00Z",
  "FailedAt": "2025-12-10T14:05:00Z",
  "ErrorMessage": "PowerService unavailable",
  "RetryCount": 3
}
```

- Stores failed extractions for later reprocessing
- Automatic retry with exponential backoff (max 9 attempts)
- Human intervention flag after exhausting retries

## Running Tests

### Unit Tests

```powershell
cd src\power-position-tracker-tests
dotnet test --logger "console;verbosity=detailed"
```

**Coverage includes:**
- `ExecutionAuditLoggerTests` - Audit trail functionality
- `LocalTimeProviderTests` - BST/GMT timezone handling
- `PowerTradeProviderTests` - API interaction and retry logic
- `PositionAggregatorTests` - Hourly aggregation logic
- `ReportWriterTests` - CSV file generation

### Integration Tests

```powershell
cd src\power-position-tracker-integration-tests
dotnet test --logger "console;verbosity=detailed"
```

**Coverage includes:**
- `PowerPositionWorkerTests` - End-to-end extraction workflows
- Multi-layer exception handling validation
- DLQ processing scenarios

### Run All Tests

```powershell
# From solution root
cd src\power-position-tracker
dotnet test --logger "console;verbosity=detailed"
```

## Development Patterns

### Exception Handling (5 Layers)

1. **Service-level** (`ExecuteAsync`) - Never crash, log & restart
2. **Extraction-level** - Retry with exponential backoff (3 attempts)
3. **DLQ Processing** - Continue on individual failures
4. **File I/O** - Fallback logging mechanisms
5. **Startup Validation** - Fail fast on configuration errors

### BST/DST Validation

```csharp
if (periods.Count != 24) {
    throw new InvalidOperationException(
        $"PowerService returned {periods.Count} periods instead of 24"
    );
}
```

### Directory Separation

- **OutputDirectory**: Business CSV files
- **AuditDirectory**: Operational logs (monthly rotation)
- **DlqDirectory**: Failed extraction recovery

## Deployment

### Docker

```powershell
# Build image
docker build -t power-position-tracker .

# Run container
docker run -d `
  -v C:\PowerReports:C:\PowerReports `
  -e PowerPositionSettings__OutputDirectory="C:\PowerReports\Output" `  
  -e PowerPositionSettings__AuditDirectory="C:\PowerReports\Audit" `
  -e PowerPositionSettings__DlqDirectory="C:\PowerReports\Dlq" `
  power-position-tracker
```

### Kubernetes (Local with Minikube)

Use the automated deployment script for local Kubernetes development:

```powershell
# Full deployment (installs prerequisites, builds image, deploys to minikube)
.\scripts\local-startup.ps1

```

**What the script does:**
1. Installs prerequisites (Chocolatey, Docker Desktop, Minikube, kubectl, Helm)
2. Starts Minikube cluster with profile `power-position-dev`
3. Builds Docker image and loads it into Minikube
4. Creates required host path directories in Minikube node
5. Deploys application using Helm chart
6. Displays deployment status and useful commands

**Useful post-deployment commands:**
```powershell
# View application logs
kubectl logs -f -l app.kubernetes.io/name=power-position-service -n default --tail=100

# Check pod status
kubectl get pods -n default -l app.kubernetes.io/name=power-position-service

# Delete deployment
helm uninstall power-position -n default

# Stop Minikube
minikube stop --profile=power-position-dev

# SSH into Minikube node
minikube ssh --profile=power-position-dev
```

## Troubleshooting

### Service Won't Start

```powershell
# Validate configuration
.\scripts\local-startup.ps1 -ValidateOnly

# Check directory permissions
Test-Path C:\PowerReports\Output -PathType Container
```

### Environment Variable Stuck

```powershell
# Clear runtime override
$env:DOTNET_RUNTIME = $null

# Verify cleared
Get-ChildItem env:DOTNET_RUNTIME
```

## Architecture

```
PowerPositionWorker (BackgroundService)
    ├── PowerTradeProvider → PowerService.dll API
    ├── PositionAggregator → Hourly aggregation
    ├── ReportWriter → CSV generation
    ├── ExecutionAuditLogger → Audit trail
    └── DeadLetterQueue → Failed extraction recovery
```

## Dependencies

- **NodaTime** - Accurate timezone handling (BST/GMT transitions)
- **PowerService.dll** - External trading API (provided)

## Contributing

When adding features:
1. Follow existing exception handling patterns (5 layers)
2. Add unit tests with >80% coverage
3. Update integration tests for workflow changes
4. Document configuration changes in this README
5. Test BST transition scenarios (March/October)

