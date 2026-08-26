param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "VivantValley.csproj"
$outputDirectory = Join-Path $projectRoot "bin\$Configuration\net6.0"
$distRoot = Join-Path $projectRoot "dist"
$packageDirectory = Join-Path $distRoot "VivantValley"
$zipPath = Join-Path $distRoot "VivantValley-$Configuration.zip"
$backendArtifacts = Join-Path $projectRoot "artifacts\backend"

$buildArguments = @("build", $projectFile, "-c", $Configuration)
if (-not [string]::IsNullOrWhiteSpace($GamePath)) {
    $buildArguments += "/p:GamePath=$GamePath"
}

& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

$distRootFull = [System.IO.Path]::GetFullPath($distRoot)
$packageFull = [System.IO.Path]::GetFullPath($packageDirectory)
if (-not $packageFull.StartsWith($distRootFull + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a package directory outside dist: $packageFull"
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$requiredFiles = @("VivantValley.dll", "manifest.json")
foreach ($fileName in $requiredFiles) {
    $source = Join-Path $outputDirectory $fileName
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Required build output is missing: $source"
    }
    Copy-Item -LiteralPath $source -Destination $packageDirectory
}

$pdbPath = Join-Path $outputDirectory "VivantValley.pdb"
if (Test-Path -LiteralPath $pdbPath) {
    Copy-Item -LiteralPath $pdbPath -Destination $packageDirectory
}

$i18nPath = Join-Path $outputDirectory "i18n"
if (Test-Path -LiteralPath $i18nPath) {
    Copy-Item -LiteralPath $i18nPath -Destination $packageDirectory -Recurse
}

$socialAssetsPath = Join-Path $outputDirectory "assets\social"
if (Test-Path -LiteralPath $socialAssetsPath) {
    $packageAssetsPath = Join-Path $packageDirectory "assets"
    New-Item -ItemType Directory -Path $packageAssetsPath -Force | Out-Null
    Copy-Item -LiteralPath $socialAssetsPath -Destination $packageAssetsPath -Recurse
}

$storyAssetsPath = Join-Path $outputDirectory "assets\stories"
if (Test-Path -LiteralPath $storyAssetsPath) {
    $packageAssetsPath = Join-Path $packageDirectory "assets"
    New-Item -ItemType Directory -Path $packageAssetsPath -Force | Out-Null
    Copy-Item -LiteralPath $storyAssetsPath -Destination $packageAssetsPath -Recurse
}

if (-not (Test-Path -LiteralPath $backendArtifacts -PathType Container)) {
    throw "Bundled backend artifacts are missing: $backendArtifacts. Build the backend before packaging."
}

$backendPlatforms = @(Get-ChildItem -LiteralPath $backendArtifacts -Directory)
if ($backendPlatforms.Count -eq 0) {
    throw "No bundled backend platform directories were found under: $backendArtifacts"
}

$packageBackendPath = Join-Path $packageDirectory "backend"
Copy-Item -LiteralPath $backendArtifacts -Destination $packageBackendPath -Recurse -Force

$windowsBackendExecutable = Join-Path $packageBackendPath "win-x64\VivantValley.LangGraph.exe"
if (-not (Test-Path -LiteralPath $windowsBackendExecutable -PathType Leaf)) {
    throw "The Windows bundled backend is missing from the package: $windowsBackendExecutable"
}

$backendReadme = Join-Path $projectRoot "backend\README.md"
if (Test-Path -LiteralPath $backendReadme) {
    $packageBackendPath = Join-Path $packageDirectory "backend"
    New-Item -ItemType Directory -Path $packageBackendPath -Force | Out-Null
    Copy-Item -LiteralPath $backendReadme -Destination $packageBackendPath -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Package directory: $packageDirectory"
Write-Host "Package archive:   $zipPath"
