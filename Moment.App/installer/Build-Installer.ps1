param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64", "x86", "ARM64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "Moment.App.csproj"
$output = Join-Path $projectRoot "dist"
$staging = Join-Path $output "_published-app"
$installer = Join-Path $output "MomentSetup-$Platform.exe"
$script = Join-Path $PSScriptRoot "MomentSetup.nsi"
$nsis = (Get-Command makensis.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($nsis)) {
    $knownNsis = @(
        "C:\Program Files (x86)\NSIS\makensis.exe",
        "C:\Program Files\NSIS\makensis.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    $nsis = [string]$knownNsis
}

if ([string]::IsNullOrWhiteSpace($nsis)) {
    throw "NSIS makensis.exe is required to create the real Windows installer. Install NSIS and run this command again."
}
if (-not (Test-Path -LiteralPath $script)) {
    throw "The NSIS installer definition is missing: $script"
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$outputRoot = (Resolve-Path -LiteralPath $output).Path
$runningFromOutput = Get-Process -Name Moment -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($outputRoot, [StringComparison]::OrdinalIgnoreCase) }
if ($runningFromOutput) {
    throw "Moment is running from dist. Close that test instance before rebuilding the installer."
}

# dist is a hand-off directory. Remove its previous contents before publishing
# and leave only the finished installer at the end.
foreach ($artifact in Get-ChildItem -LiteralPath $outputRoot -Force -ErrorAction SilentlyContinue) {
    Remove-Item -LiteralPath $artifact.FullName -Recurse -Force
}
New-Item -ItemType Directory -Path $staging -Force | Out-Null

$rid = switch ($Platform) {
    "ARM64" { "win-arm64" }
    "x86" { "win-x86" }
    default { "win-x64" }
}

Write-Host "Publishing unpackaged, self-contained Moment ($rid)..."
dotnet publish $project -c $Configuration -p:Platform=$Platform -p:RuntimeIdentifier=$rid `
    "-p:PublishDir=$staging\" -p:PublishReadyToRun=false -p:PublishTrimmed=false --no-restore
if ($LASTEXITCODE -ne 0) { throw "WinUI publish failed." }

$exePath = Join-Path $staging "Moment.exe"
$runtimePath = Join-Path $staging "Microsoft.WindowsAppRuntime.dll"
$encoderPath = Join-Path $staging "Tools\ffmpeg.exe"
if (-not (Test-Path -LiteralPath $exePath) -or -not (Test-Path -LiteralPath $runtimePath) -or -not (Test-Path -LiteralPath $encoderPath)) {
    throw "The publish output is missing the Moment executable, bundled Windows App SDK runtime, or WebM encoder."
}

$payloadForNsis = $staging -replace '\\', '/'
$installerForNsis = $installer -replace '\\', '/'
Write-Host "Building a real NSIS installer for Moment with an install wizard, Start menu shortcut, and uninstaller..."
& $nsis /V2 "/DPAYLOAD=$payloadForNsis" "/DOUTFILE=$installerForNsis" $script
if ($LASTEXITCODE -ne 0) { throw "NSIS could not build the Windows installer." }

if (-not (Test-Path -LiteralPath $installer)) {
    throw "NSIS completed without creating the installer: $installer"
}

$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
Write-Host "Real Windows installer created: $installer"
Write-Host "SHA256: $hash"
Write-Host "Installer technology: NSIS (not a 7-Zip self-extracting archive)."

# Remove the published payload and every intermediate so dist contains only
# the installer users should download.
$installerFullPath = (Resolve-Path -LiteralPath $installer).Path
foreach ($artifact in Get-ChildItem -LiteralPath $outputRoot -Force) {
    if ($artifact.FullName -ne $installerFullPath) {
        Remove-Item -LiteralPath $artifact.FullName -Recurse -Force
    }
}
