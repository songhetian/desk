$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$distDir = Join-Path $root "dist"
$clientDir = Join-Path $distDir "Client"
$studioDir = Join-Path $distDir "Studio"

function Remove-DirectoryWithRetry {
    param([string]$Path, [int]$MaxRetries = 5)
    
    if (-not (Test-Path $Path)) { return }
    
    for ($i = 1; $i -le $MaxRetries; $i++) {
        try {
            Get-ChildItem $Path -Recurse -Force | ForEach-Object {
                $_.Attributes = "Normal"
            }
            Remove-Item $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($i -lt $MaxRetries) {
                Write-Host "  Retry $i/$MaxRetries in 1s..." -ForegroundColor Yellow
                Start-Sleep -Seconds 1
            } else {
                Write-Host "  Failed to delete: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "  Please close the app and try again" -ForegroundColor Yellow
                throw
            }
        }
    }
}

function Stop-ProcessIfRunning {
    param([string]$ProcessName)
    $procs = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "  $ProcessName is running, stopping..." -ForegroundColor Yellow
        $procs | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

Write-Host "=== Checking running processes ===" -ForegroundColor Cyan
Stop-ProcessIfRunning "WordGuard.Client.App"
Stop-ProcessIfRunning "WordGuard.Studio.App"

Write-Host ""
Write-Host "=== Cleaning old dist directory ===" -ForegroundColor Cyan
if (Test-Path $distDir) {
    Remove-DirectoryWithRetry $distDir
}
New-Item -ItemType Directory -Path $clientDir -Force | Out-Null
New-Item -ItemType Directory -Path $studioDir -Force | Out-Null

Write-Host ""
Write-Host "=== Publishing Client ===" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/WordGuard.Client.App/WordGuard.Client.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $clientDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Client publish failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Publishing Studio ===" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/WordGuard.Studio.App/WordGuard.Studio.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $studioDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Studio publish failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Publish Complete ===" -ForegroundColor Green
Write-Host "Client: $clientDir"
Write-Host "Studio: $studioDir"

$clientExe = Join-Path $clientDir "WordGuard.Client.App.exe"
$studioExe = Join-Path $studioDir "WordGuard.Studio.App.exe"
if (Test-Path $clientExe) {
    $size = [math]::Round((Get-Item $clientExe).Length / 1MB, 1)
    Write-Host "Client exe: $size MB" -ForegroundColor Cyan
}
if (Test-Path $studioExe) {
    $size = [math]::Round((Get-Item $studioExe).Length / 1MB, 1)
    Write-Host "Studio exe: $size MB" -ForegroundColor Cyan
}
