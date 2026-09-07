param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.local/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }

if (-not $SkipBuild) {
    & (Join-Path $projectRoot 'windows/build.ps1')
}

$assembly = Join-Path $projectRoot 'windows/Suiyi.Windows/bin/Release/net10.0-windows/win-x64/Suiyi.dll'
if (-not (Test-Path -LiteralPath $assembly)) {
    throw 'Development output is missing. Run this script without -SkipBuild first.'
}

# The GUI apphost avoids opening a console and must be visible for manual review.
# Give only this child the bundled runtime path; never publish or replace app/.
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = [IO.Path]::ChangeExtension($assembly, '.exe')
$start.Arguments = '--dev'
$start.WorkingDirectory = $projectRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Normal
if (Test-Path -LiteralPath $localSdk) {
    $start.EnvironmentVariables['DOTNET_ROOT'] = Split-Path -Parent $localSdk
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = Split-Path -Parent $localSdk
}
$process = [Diagnostics.Process]::Start($start)
Write-Output ('Development app started (PID ' + $process.Id + '): ' + $assembly)
