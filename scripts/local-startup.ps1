# Description: PowerShell script to install prerequisites, build Docker image, and deploy to local minikube

<#
.SYNOPSIS
    Local development startup script for Power Position Tracker
.DESCRIPTION
    Installs prerequisites (minikube, helm, kubectl), builds Docker image, 
    and deploys the application to local minikube using Helm chart
.PARAMETER SkipPrerequisites
    Skip prerequisite installation check
.PARAMETER SkipBuild
    Skip Docker image build
.PARAMETER Environment
    Target environment (dev or prod)
.EXAMPLE
    .\local-startup.ps1
.EXAMPLE
    .\local-startup.ps1 -SkipPrerequisites -Environment prod
#>

[CmdletBinding()]
param(
    [Parameter()]
    [switch]$SkipPrerequisites,
    
    [Parameter()]
    [switch]$SkipBuild,
    
    [Parameter()]
    [ValidateSet('dev', 'prod')]
    [string]$Environment = 'dev'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Script configuration
$ScriptRoot = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $ScriptRoot
$HelmChartPath = Join-Path $ProjectRoot "helm\power-position-service"
$DockerImageName = "power-position-tracker"
$DockerImageTag = "latest"
$MinikubeProfile = "power-position-dev"
$HelmReleaseName = "power-position"
$Namespace = "default"

# Color output functions
function Write-Step {
    param([string]$Message)
    Write-Host "`n===> $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-ErrorMessage {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

function Write-WarningMessage {
    param([string]$Message)
    Write-Host "⚠ $Message" -ForegroundColor Yellow
}

# Prerequisite check functions
function Test-CommandExists {
    param([string]$Command)
    $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

function Install-Chocolatey {
    Write-Step "Installing Chocolatey package manager..."
    
    if (Test-CommandExists 'choco') {
        Write-Success "Chocolatey already installed"
        return
    }
    
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
    Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))
    
    if (Test-CommandExists 'choco') {
        Write-Success "Chocolatey installed successfully"
    } else {
        throw "Failed to install Chocolatey"
    }
}

function Install-Minikube {
    Write-Step "Installing Minikube..."
    
    if (Test-CommandExists 'minikube') {
        Write-Success "Minikube already installed ($(minikube version --short))"
        return
    }
    
    choco install minikube -y
    refreshenv
    
    if (Test-CommandExists 'minikube') {
        Write-Success "Minikube installed successfully"
    } else {
        throw "Failed to install Minikube. Please restart PowerShell and run script again."
    }
}

function Install-Kubectl {
    Write-Step "Installing kubectl..."
    
    if (Test-CommandExists 'kubectl') {
        $version = (kubectl version --client -o json 2>$null | ConvertFrom-Json).clientVersion.gitVersion
        Write-Success "kubectl already installed ($version)"
        return
    }
    
    choco install kubernetes-cli -y
    refreshenv
    
    if (Test-CommandExists 'kubectl') {
        Write-Success "kubectl installed successfully"
    } else {
        throw "Failed to install kubectl. Please restart PowerShell and run script again."
    }
}

function Install-Helm {
    Write-Step "Installing Helm..."
    
    if (Test-CommandExists 'helm') {
        Write-Success "Helm already installed ($(helm version --short))"
        return
    }
    
    choco install kubernetes-helm -y
    refreshenv
    
    if (Test-CommandExists 'helm') {
        Write-Success "Helm installed successfully"
    } else {
        throw "Failed to install Helm. Please restart PowerShell and run script again."
    }
}

function Install-Docker {
    Write-Step "Checking Docker installation..."
    
    if (Test-CommandExists 'docker') {
        Write-Success "Docker already installed ($(docker --version))"
        return
    }
    
    Write-WarningMessage "Docker Desktop is not installed"
    Write-Host "Please install Docker Desktop manually from: https://www.docker.com/products/docker-desktop"
    Write-Host "After installation, restart PowerShell and run this script again."
    throw "Docker Desktop required"
}

function Start-MinikubeCluster {
    Write-Step "Starting Minikube cluster..."
    
    # Temporarily allow errors for minikube commands (cluster might be corrupted)
    $prevErrorPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    
    try {
        # Check if the specified profile exists in the profile list
        $profileList = minikube profile list 2>&1 | Out-String
        $profileExists = $profileList -match $MinikubeProfile
        
        if ($profileExists) {
            Write-Host "Found existing Minikube cluster: $MinikubeProfile"
            
            # Check current status
            $statusOutput = minikube status --profile=$MinikubeProfile 2>&1 | Out-String
            $isRunning = $statusOutput -match "host: Running" -and $statusOutput -match "kubelet: Running"
            
            if ($isRunning) {
                Write-Success "Minikube cluster '$MinikubeProfile' already running"
                return
            }
            
            # Try to start existing cluster
            Write-Host "Attempting to start cluster..."
            $startOutput = minikube start --profile=$MinikubeProfile 2>&1 | Out-String
            Write-Host $startOutput
            
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Minikube cluster started successfully"
                return
            }
            
            # If start failed, cluster is corrupted - delete and recreate
            Write-WarningMessage "Cluster in bad state. Deleting and recreating..."
            $deleteOutput = minikube delete --profile=$MinikubeProfile 2>&1
            Start-Sleep -Seconds 3
            
            Write-Host "Creating new Minikube cluster: $MinikubeProfile"
            minikube start --profile=$MinikubeProfile --driver=docker --cpus=2 --memory=4096 --disk-size=20g
            
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create Minikube cluster"
            }
            Write-Success "Minikube cluster created successfully"
            return
        }
        
        # Profile doesn't exist, create new cluster
        Write-Host "Creating new Minikube cluster with profile: $MinikubeProfile"
        minikube start --profile=$MinikubeProfile --driver=docker --cpus=2 --memory=4096 --disk-size=20g
        
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to start Minikube cluster"
        }
        
        Write-Success "Minikube cluster started successfully"
    }
    finally {
        $ErrorActionPreference = $prevErrorPref
    }
}

function Enable-MinikubeAddons {
    Write-Step "Enabling Minikube addons..."
    
    minikube addons enable storage-provisioner --profile=$MinikubeProfile
    minikube addons enable default-storageclass --profile=$MinikubeProfile
    
    Write-Success "Minikube addons enabled"
}

function Set-DockerEnvironment {
    Write-Step "Configuring Docker environment for Minikube..."
    
    # Set Docker environment to use Minikube's Docker daemon
    & minikube -p $MinikubeProfile docker-env --shell powershell | Invoke-Expression
    
    Write-Success "Docker environment configured to use Minikube registry"
}

function Build-DockerImage {
    Write-Step "Building Docker image..."
    
    Set-Location $ProjectRoot
    
    Write-Host "Building image: ${DockerImageName}:${DockerImageTag}"
    docker build -t "${DockerImageName}:${DockerImageTag}" -f Dockerfile .
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build Docker image"
    }
    
    Write-Success "Docker image built successfully"
    
    # Load image into Minikube
    Write-Host "Loading image into Minikube registry..."
    minikube -p $MinikubeProfile image load "${DockerImageName}:${DockerImageTag}"
    
    if ($LASTEXITCODE -ne 0) {
        Write-WarningMessage "Failed to load image into Minikube, but continuing..."
    } else {
        Write-Success "Image loaded into Minikube"
    }
}

function New-HostPathDirectories {
    Write-Step "Creating host path directories in Minikube..."
    
    $envPath = if ($Environment -eq 'dev') { 'dev' } else { 'prod' }
    $basePath = "/mnt/power-reports-$envPath"
    
    $directories = @(
        "$basePath/output",
        "$basePath/audit",
        "$basePath/dlq"
    )
    
    foreach ($dir in $directories) {
        Write-Host "Creating directory: $dir"
        $sshCommand = "sudo mkdir -p $dir; sudo chmod 777 $dir"
        minikube ssh --profile=$MinikubeProfile -- $sshCommand
    }
    
    Write-Success "Host path directories created"
}

function Deploy-HelmChart {
    Write-Step "Deploying Helm chart..."
    
    $valuesFile = Join-Path $HelmChartPath "values-$Environment.yaml"
    
    if (-not (Test-Path $valuesFile)) {
        throw "Values file not found: $valuesFile"
    }
    
    # Check if release exists
    $releaseExists = helm list --namespace=$Namespace --filter="^${HelmReleaseName}$" -o json | ConvertFrom-Json
    
    if ($releaseExists) {
        Write-Host "Upgrading existing Helm release: $HelmReleaseName"
        helm upgrade $HelmReleaseName $HelmChartPath `
            --namespace=$Namespace `
            --values=$valuesFile `
            --wait `
            --timeout=5m
    } else {
        Write-Host "Installing new Helm release: $HelmReleaseName"
        helm install $HelmReleaseName $HelmChartPath `
            --namespace=$Namespace `
            --values=$valuesFile `
            --wait `
            --timeout=5m
    }
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to deploy Helm chart"
    }
    
    Write-Success "Helm chart deployed successfully"
}

function Show-DeploymentStatus {
    Write-Step "Deployment Status"
    
    Write-Host "`nPods:"
    kubectl get pods --namespace=$Namespace -l "app.kubernetes.io/name=power-position-service"
    
    Write-Host "`nPersistent Volumes:"
    kubectl get pv
    
    Write-Host "`nPersistent Volume Claims:"
    kubectl get pvc --namespace=$Namespace -l "app.kubernetes.io/name=power-position-service"
    
    Write-Host "`n"
    Write-Success "Deployment completed successfully!"
    
    Write-Host "`nUseful commands:"
    Write-Host "  View logs:      kubectl logs -f -l app.kubernetes.io/name=power-position-service -n $Namespace"
    Write-Host "  View pod:       kubectl get pods -n $Namespace -l app.kubernetes.io/name=power-position-service"
    Write-Host "  Delete release: helm uninstall $HelmReleaseName -n $Namespace"
    Write-Host "  Stop minikube:  minikube stop --profile=$MinikubeProfile"
    Write-Host "  SSH to node:    minikube ssh --profile=$MinikubeProfile"
}

# Main execution
try {
    Write-Host @"
╔════════════════════════════════════════════════════════════╗
║   Power Position Tracker - Local Deployment Script        ║
║   Environment: $($Environment.ToUpper().PadRight(43)) ║
╚════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

    # Install prerequisites
    if (-not $SkipPrerequisites) {
        Write-Step "Checking and installing prerequisites..."
        Install-Chocolatey
        Install-Docker
        Install-Minikube
        Install-Kubectl
        Install-Helm
    } else {
        Write-WarningMessage "Skipping prerequisite checks"
    }
    
    # Start Minikube
    Start-MinikubeCluster
    Enable-MinikubeAddons
    
    # Configure Docker to use Minikube registry
    Set-DockerEnvironment
    
    # Build Docker image
    if (-not $SkipBuild) {
        Build-DockerImage
    } else {
        Write-WarningMessage "Skipping Docker image build"
    }
    
    # Create required directories
    New-HostPathDirectories
    
    # Deploy with Helm
    Deploy-HelmChart
    
    # Show status
    Show-DeploymentStatus
    
} catch {
    Write-ErrorMessage "Deployment failed: $_"
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}
