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

## Execution Flow

**On Startup (ExecuteAsync):**
1. Service initializes as BackgroundService with configured settings
2. Process Dead Letter Queue (DLQ) - retry all previously failed extractions
   - Each DLQ item is retried with `RunSingleExtractionAsync()`
   - Successful recoveries are removed from DLQ
   - Failed retries are re-queued with incremented retry count
3. Run initial extraction immediately (before first interval timer)

**Periodic Extraction Loop:**
4. Wait for next scheduled interval (default: 5 minutes)
5. Execute `RunExtractionWithRetryAsync()`:
   - Determine target date from runtime config
   - Attempt extraction up to 3 times with 10-second delays
   - On success: exit and wait for next interval
   - On failure: add to DLQ and wait for next interval
6. Repeat steps 4-5 until service shutdown

**Service never crashes** - exceptions are caught, logged, and processing continues


## RunExtractionWithRetryAsync Method
1. Determine extraction target date based on runtime context (DOTNET_RUNTIME env var → config → DateTime.UtcNow)
2. Convert extraction time from UTC to London Local Time
3. Loop through retry attempts (default: 3 attempts)
   - Call `RunSingleExtractionAsync()` for each attempt
   - If successful, exit immediately
   - If failed and more attempts remain, wait for retry delay (default: 10 seconds)
4. If all retry attempts exhausted, create DLQ entry for later recovery

## RunSingleExtractionAsync Method
1. Retrieve power trades from PowerService API for target date
2. Aggregate positions by hour (validates 24 periods)
3. Write CSV report to OutputDirectory
4. Determine status (Done, RecoveredFromDLQ, RetryAttempt, Failed, Cancelled)
5. Log audit record with extraction details (always runs in finally block) 

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

### Usage

```powershell
# Basic usage - starts service with validation
.\scripts\local-startup.ps1

# With specific runtime override (for testing/debugging)
.\scripts\local-startup.ps1 -RuntimeOverride "2025-12-10T14:30:00Z"

```





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
    "RetryDelaySeconds": 10,
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

'src/power-position-tracker/docs/' contains three key directories:
 - `Output` - Business CSV reports
 - `Audit` - Execution audit logs
 - `Dlq` - Dead letter queue for failed extractions

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
2025-12-10T14:05:00Z,2025-12-10,Success,0,,src/power-position-tracker/docs/Output/PowerPosition_20251210_1405.csv
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
- `PowerPositionWorkerTests` - Core extraction workflow
- `PositionAggregatorTests` - Hourly aggregation logic

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


**The script will automatically:**

✅ Verify/install Chocolatey (if needed)
✅ Verify/install Docker Desktop (if needed)
✅ Verify/install Minikube (if needed)
✅ Verify/install kubectl (if needed)
✅ Verify/install Helm (if needed)
✅ Create a fresh Minikube cluster with profile power-position-dev
✅ Enable storage addons
✅ Configure Docker to use Minikube's registry
✅ Build the Docker image
✅ Load image into Minikube
✅ Create required directories
✅ Deploy the Helm chart
✅ Start the application

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


