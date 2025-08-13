#!/usr/bin/env pwsh
#Requires -Version 5.0

<#
.SYNOPSIS
    Build script for Saturn CLI Tool
.DESCRIPTION
    Builds, tests, and packages Saturn for NuGet distribution
.PARAMETER Configuration
    Build configuration (Debug/Release). Default: Release
.PARAMETER Version
    Package version override. If not specified, uses version from csproj
.PARAMETER NoPack
    Skip creating NuGet package
.PARAMETER Clean
    Clean build output before building
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Debug
    .\build.ps1 -Version 1.0.1
    .\build.ps1 -Clean
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [string]$Version = "",
    
    [switch]$NoPack,
    
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

try {
    $projectPath = $PSScriptRoot
    $csprojPath = Join-Path $projectPath "Saturn.csproj"
    
    if (-not (Test-Path $csprojPath)) {
        throw "Project file not found: $csprojPath"
    }
    
    # Clean if requested
    if ($Clean) {
        Write-Header "Cleaning Solution"
        
        $folders = @("bin", "obj", "nupkg")
        foreach ($folder in $folders) {
            $path = Join-Path $projectPath $folder
            if (Test-Path $path) {
                Remove-Item -Path $path -Recurse -Force
                Write-Success "Removed $folder"
            }
        }
    }
    
    # Restore dependencies
    Write-Header "Restoring Dependencies"
    dotnet restore $csprojPath
    if ($LASTEXITCODE -ne 0) { throw "Restore failed" }
    Write-Success "Dependencies restored"
    
    # Build
    Write-Header "Building Saturn ($Configuration)"
    $buildArgs = @(
        "build",
        $csprojPath,
        "-c", $Configuration,
        "--no-restore"
    )
    
    if ($Version) {
        $buildArgs += "-p:Version=$Version"
    }
    
    dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    Write-Success "Build completed"
    
    # Run tests if they exist
    $testProjects = Get-ChildItem -Path $PSScriptRoot -Filter "*Tests.csproj" -Recurse
    if ($testProjects) {
        Write-Header "Running Tests"
        foreach ($testProject in $testProjects) {
            Write-Host "Testing: $($testProject.Name)"
            dotnet test $testProject.FullName -c $Configuration --no-build
            if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
        }
        Write-Success "All tests passed"
    }
    
    # Pack NuGet package
    if (-not $NoPack) {
        Write-Header "Creating NuGet Package"
        
        $packArgs = @(
            "pack",
            $csprojPath,
            "-c", $Configuration,
            "--no-build"
        )
        
        if ($Version) {
            $packArgs += "-p:Version=$Version"
        }
        
        dotnet @packArgs
        if ($LASTEXITCODE -ne 0) { throw "Pack failed" }
        
        $nupkgPath = Join-Path $projectPath "nupkg"
        if (Test-Path $nupkgPath) {
            $packages = Get-ChildItem -Path $nupkgPath -Filter "*.nupkg"
            foreach ($package in $packages) {
                Write-Success "Created package: $($package.Name)"
                Write-Host "  Size: $([Math]::Round($package.Length / 1KB, 2)) KB"
            }
        }
    }
    
    Write-Header "Build Completed Successfully!"
    
    # Instructions for publishing
    if (-not $NoPack) {
        Write-Host ""
        Write-Host "To test the tool locally:" -ForegroundColor Yellow
        Write-Host "  dotnet tool install --global --add-source ./nupkg SaturnAgent"
        Write-Host ""
        Write-Host "To publish to NuGet.org:" -ForegroundColor Yellow
        Write-Host "  dotnet nuget push ./nupkg/*.nupkg -k YOUR_API_KEY -s https://api.nuget.org/v3/index.json"
    }
    
} catch {
    Write-Error $_.Exception.Message
    exit 1
}