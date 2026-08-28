param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$GamePath = "",

    [ValidateSet("win-x64", "osx-x64", "osx-arm64")]
    [string]$BackendPlatform = "win-x64"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot "VivantValley.csproj"
$outputDirectory = Join-Path $projectRoot "bin\$Configuration\net6.0"
$distRoot = Join-Path $projectRoot "dist"
$packageDirectory = Join-Path $distRoot "VivantValley"
$backendArtifacts = Join-Path $projectRoot "artifacts\backend"
$platformLabel = switch ($BackendPlatform) {
    "win-x64" { "Windows-x64" }
    "osx-x64" { "macOS-Intel" }
    "osx-arm64" { "macOS-AppleSilicon" }
    default { throw "Unsupported backend platform: $BackendPlatform" }
}
$zipPath = Join-Path $distRoot "VivantValley-$platformLabel-$Configuration.zip"

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

$backendSource = Join-Path $backendArtifacts $BackendPlatform
if (-not (Test-Path -LiteralPath $backendSource -PathType Container)) {
    throw "The requested bundled backend platform is missing: $backendSource"
}

$packageBackendPath = Join-Path $packageDirectory "backend"
New-Item -ItemType Directory -Path $packageBackendPath -Force | Out-Null
Copy-Item -LiteralPath $backendSource -Destination $packageBackendPath -Recurse -Force

$backendExecutableName = if ($BackendPlatform -eq "win-x64") {
    "VivantValley.LangGraph.exe"
}
else {
    "VivantValley.LangGraph"
}
$backendExecutable = Join-Path $packageBackendPath "$BackendPlatform\$backendExecutableName"
if (-not (Test-Path -LiteralPath $backendExecutable -PathType Leaf)) {
    throw "The requested bundled backend executable is missing from the package: $backendExecutable"
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

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entryNames = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $expectedExecutableEntry = "VivantValley/backend/$BackendPlatform/$backendExecutableName"
    if ($entryNames -notcontains $expectedExecutableEntry) {
        throw "Packaged archive is missing its expected backend executable: $expectedExecutableEntry"
    }

    $unexpectedBackendEntries = @($entryNames | Where-Object {
        $isBackendEntry = $_.StartsWith("VivantValley/backend/", [StringComparison]::OrdinalIgnoreCase)
        $isSelectedPlatformEntry = $_.StartsWith(
            "VivantValley/backend/$BackendPlatform/",
            [StringComparison]::OrdinalIgnoreCase)
        $isBackendReadme = $_.Equals("VivantValley/backend/README.md", [StringComparison]::OrdinalIgnoreCase)
        $isBackendEntry -and -not $isSelectedPlatformEntry -and -not $isBackendReadme
    })
    if ($unexpectedBackendEntries.Count -gt 0) {
        throw "Packaged archive contains files for another backend platform: $($unexpectedBackendEntries -join ', ')"
    }

    if ($entryNames -contains "VivantValley/config.json") {
        throw "Packaged archive must not contain config.json."
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Package directory: $packageDirectory"
Write-Host "Package archive:   $zipPath"
