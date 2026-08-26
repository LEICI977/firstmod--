param(
    [string]$PythonPath = "",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $projectRoot "artifacts\backend"
}

if ([string]::IsNullOrWhiteSpace($PythonPath)) {
    $venvPython = Join-Path $projectRoot ".langgraph-venv\Scripts\python.exe"
    if (Test-Path -LiteralPath $venvPython) {
        $PythonPath = $venvPython
    }
    else {
        $PythonPath = "python"
    }
}

$pythonCommand = Get-Command $PythonPath -ErrorAction SilentlyContinue
if ($null -eq $pythonCommand -and -not (Test-Path -LiteralPath $PythonPath)) {
    throw "Python was not found: $PythonPath"
}

$pyinstaller = & $PythonPath -c "import PyInstaller; print(PyInstaller.__path__[0])"
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller is not installed for $PythonPath. Install it with: $PythonPath -m pip install pyinstaller"
}

$platformDirectory = if ($IsWindows -or $env:OS -eq "Windows_NT") {
    "win-x64"
} elseif ($IsMacOS) {
    if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq "Arm64") { "osx-arm64" } else { "osx-x64" }
} else {
    if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq "Arm64") { "linux-arm64" } else { "linux-x64" }
}

$outputDirectory = Join-Path $OutputRoot $platformDirectory
if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$workRoot = Join-Path $projectRoot "backend-build-current"
if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null

& $PythonPath -m PyInstaller --noconfirm --clean --onedir `
    --name VivantValley.LangGraph `
    --distpath (Join-Path $workRoot "dist") `
    --workpath (Join-Path $workRoot "work") `
    --specpath $workRoot `
    (Join-Path $projectRoot "langgraph_service\app.py")
if ($LASTEXITCODE -ne 0) {
    throw "PyInstaller failed with exit code $LASTEXITCODE."
}

$builtBackend = Join-Path $workRoot "dist\VivantValley.LangGraph"
Get-ChildItem -LiteralPath $builtBackend -Force | Copy-Item -Destination $outputDirectory -Recurse -Force
if (-not ($IsWindows -or $env:OS -eq "Windows_NT")) {
    $binary = Join-Path $outputDirectory "VivantValley.LangGraph"
    if (Test-Path -LiteralPath $binary) {
        & chmod +x $binary
    }
}

Remove-Item -LiteralPath $workRoot -Recurse -Force
Write-Host "Bundled LangGraph backend: $outputDirectory"
