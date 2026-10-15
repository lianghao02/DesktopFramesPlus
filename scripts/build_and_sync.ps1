[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$codeProj = Join-Path $repoRoot "Code\Desktop Frames\Desktop Frames.csproj"
$portableBin = Join-Path $repoRoot "Code\Desktop Frames\bin\PortablePublish\win-x64"
$distTarget = Join-Path $repoRoot "dist\DesktopFramesPlus"
$msbuildPath = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe"

if (-not (Test-Path $msbuildPath)) {
    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64\MSBuild.exe"
    )
    foreach ($cand in $candidates) {
        if (Test-Path $cand) {
            $msbuildPath = $cand
            break
        }
    }
}

if (-not $SkipBuild) {
    Write-Host "[1/3] Publishing Portable-win-x64 via MSBuild..." -ForegroundColor Cyan
    & $msbuildPath $codeProj /restore /t:Publish /p:PublishProfile=Portable-win-x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /v:minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    Write-Host "  -> Publish succeeded." -ForegroundColor Green
} else {
    Write-Host "[1/3] Skip publish (SkipBuild specified)." -ForegroundColor Yellow
}

Write-Host "[2/3] Syncing portable binaries to dist\DesktopFramesPlus..." -ForegroundColor Cyan
if (-not (Test-Path $distTarget)) {
    New-Item -ItemType Directory -Path $distTarget -Force | Out-Null
}

# 複製所有發布檔案，嚴格排除 Profiles/
$items = Get-ChildItem -Path $portableBin
foreach ($item in $items) {
    if ($item.Name -eq "Profiles") {
        Write-Host "  [Preserved] Profiles/ (User Data)" -ForegroundColor DarkYellow
        continue
    }

    $destPath = Join-Path $distTarget $item.Name
    if ($item.PSIsContainer) {
        if (-not (Test-Path $destPath)) { New-Item -ItemType Directory -Path $destPath -Force | Out-Null }
        Copy-Item -Path (Join-Path $item.FullName "*") -Destination $destPath -Recurse -Force
    } else {
        Copy-Item -Path $item.FullName -Destination $destPath -Force
    }
}

Write-Host "[3/3] Sync completed successfully! User profiles 100% preserved." -ForegroundColor Green
