[CmdletBinding()]
param(
    [string]$GameDir = 'D:\Steam\steamapps\common\Lunarium',
    [string]$DotNetPath = '',
    [string]$MelonLoaderDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = $PSScriptRoot
$managedDir = Join-Path $GameDir 'Lunarium_Data\Managed'
$dataPath = Join-Path $projectRoot 'Data\collectibles.json'
$assemblyInfoPath = Join-Path $projectRoot 'src\AssemblyInfo.cs'
$distDir = Join-Path $projectRoot 'dist'
$modsDir = Join-Path $projectRoot 'dist\Mods'
$legacyBinDir = Join-Path $projectRoot 'bin'
$legacyObjDir = Join-Path $projectRoot 'obj'

function Assert-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
}

function Remove-ProjectDirectory([string]$Path) {
    $resolvedProjectRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
    $resolvedTarget = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not $resolvedTarget.StartsWith($resolvedProjectRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a directory outside the project: $resolvedTarget"
    }

    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

function Reset-ProjectDirectory([string]$Path) {
    Remove-ProjectDirectory $Path
    $resolvedTarget = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    New-Item -ItemType Directory -Force -Path $resolvedTarget | Out-Null
}

function Get-SdkLine([string]$Executable) {
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        return $null
    }

    $lines = @(& $Executable --list-sdks 2>$null)
    if ($LASTEXITCODE -ne 0 -or $lines.Count -eq 0) {
        return $null
    }

    return $lines | Select-Object -Last 1
}

Assert-File (Join-Path $managedDir 'Lunarium.dll') 'Game assembly'
Assert-File $dataPath 'Embedded collection data'
Assert-File $assemblyInfoPath 'Assembly metadata'
$database = Get-Content -LiteralPath $dataPath -Raw -Encoding UTF8 | ConvertFrom-Json
$mapCount = @($database.worlds.maps).Count
$itemCount = @($database.worlds.maps.items).Count
if ($database.gameBuildId -ne '24739334' -or $mapCount -ne 23 -or $itemCount -ne 176) {
    throw "Embedded data validation failed: build=$($database.gameBuildId), maps=$mapCount, items=$itemCount"
}

$assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw -Encoding UTF8
$versionMatch = [regex]::Match($assemblyInfo, 'MelonInfo\([^,]+,\s*"[^"]+",\s*"(?<version>[^"]+)"')
if (-not $versionMatch.Success) {
    throw "Could not read the Mod version from $assemblyInfoPath"
}
$releaseVersion = $versionMatch.Groups['version'].Value
$releaseZip = Join-Path $projectRoot "dist\LunariumItemCollectionMod-v$releaseVersion.zip"

$dotnet = $null
$sdkLine = $null
if (-not [string]::IsNullOrWhiteSpace($DotNetPath)) {
    $dotnet = [IO.Path]::GetFullPath($DotNetPath)
    Assert-File $dotnet '.NET executable'
    $sdkLine = Get-SdkLine $dotnet
    if (-not $sdkLine) {
        throw "The requested .NET executable has no SDK: $dotnet"
    }
}
else {
    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($systemDotnet) {
        $candidate = $systemDotnet.Source
        $candidateSdk = Get-SdkLine $candidate
        if ($candidateSdk) {
            $dotnet = $candidate
            $sdkLine = $candidateSdk
        }
    }

    if (-not $dotnet) {
        $cachedDotnet = Join-Path $env:LOCALAPPDATA 'LunariumItemCollectionMod\build-tools\dotnet-8\dotnet.exe'
        $candidateSdk = Get-SdkLine $cachedDotnet
        if ($candidateSdk) {
            $dotnet = $cachedDotnet
            $sdkLine = $candidateSdk
        }
    }
}

if (-not $dotnet -or -not $sdkLine) {
    throw 'A .NET SDK is required. Install .NET 8 SDK or pass -DotNetPath. This script does not download dependencies.'
}
$sdkVersion = ($sdkLine -split '\s+')[0]
$csc = Join-Path (Split-Path -Parent $dotnet) "sdk\$sdkVersion\Roslyn\bincore\csc.dll"
Assert-File $csc 'Roslyn compiler'

if ([string]::IsNullOrWhiteSpace($MelonLoaderDir)) {
    $MelonLoaderDir = Join-Path $GameDir 'MelonLoader'
}
$melonNet472 = Join-Path ([IO.Path]::GetFullPath($MelonLoaderDir)) 'net472'
Assert-File (Join-Path $melonNet472 'MelonLoader.dll') 'MelonLoader net472 reference'

$references = @(
    'mscorlib.dll',
    'netstandard.dll',
    'System.dll',
    'System.Core.dll',
    'System.Runtime.dll',
    'Lunarium.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.UI.dll',
    'UnityEngine.UIModule.dll',
    'Unity.InputSystem.dll',
    'Unity.TextMeshPro.dll',
    'Newtonsoft.Json.dll'
) | ForEach-Object { Join-Path $managedDir $_ }
$references += Join-Path $melonNet472 'MelonLoader.dll'
$references | ForEach-Object { Assert-File $_ 'Compiler reference' }

Remove-ProjectDirectory $legacyBinDir
Remove-ProjectDirectory $legacyObjDir
Reset-ProjectDirectory $distDir
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null

$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName
$outputDll = Join-Path $modsDir 'LunariumItemCollectionMod.dll'
$compilerArgs = @(
    '/noconfig',
    '/nostdlib+',
    '/target:library',
    '/langversion:latest',
    '/nullable:enable',
    '/optimize+',
    '/deterministic+',
    '/utf8output',
    '/warn:4',
    "/out:$outputDll",
    "/resource:$dataPath,LunariumItemCollectionMod.collectibles.json"
)
$compilerArgs += $references | ForEach-Object { "/reference:$_" }
$compilerArgs += $sources

Write-Host "Compiling $outputDll"
& $dotnet $csc @compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}
Assert-File $outputDll 'Compiled Mod DLL'

$archiveInputs = @(
    $modsDir,
    (Join-Path $projectRoot 'README.md'),
    (Join-Path $projectRoot 'LICENSE')
)
Compress-Archive -LiteralPath $archiveInputs -DestinationPath $releaseZip -CompressionLevel Optimal

Write-Host 'Build completed:'
Write-Host "  DLL: $modsDir\LunariumItemCollectionMod.dll"
Write-Host "  Release: $releaseZip"
