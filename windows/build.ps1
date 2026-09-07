param([switch]$Publish, [switch]$Package)
$ErrorActionPreference = 'Stop'
if ($Package) { $Publish = $true }
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.local/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.local/nuget-http'
$project = Join-Path $PSScriptRoot 'Suiyi.Windows/Suiyi.Windows.csproj'
$appDirectory = Join-Path $projectRoot 'app'
$iconFile = Join-Path $projectRoot 'assets/app-icon/app.ico'
if (-not (Test-Path -LiteralPath $iconFile)) { throw 'App icon is missing: assets/app-icon/app.ico' }
& $dotnetCommand restore $project --configfile (Join-Path $PSScriptRoot 'NuGet.Config') "-p:SelfContained=$($Publish.IsPresent.ToString().ToLowerInvariant())"
if ($LASTEXITCODE -ne 0) { throw 'Windows dependency restore failed.' }
if ($Publish) {
    & $dotnetCommand publish $project -c Release --self-contained true --no-restore -o $appDirectory
} else {
    & $dotnetCommand build $project -c Release --no-restore
}
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
if (-not $Publish) { return }

# The tiny root launcher uses the .NET Framework compiler included with Windows.
# All application dependencies remain in app/; no global SDK installation is needed.
$launcherCompiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $launcherCompiler)) { throw 'The Windows .NET Framework compiler was not found.' }
$launcherFile = Join-Path $projectRoot '随译.exe'
& $launcherCompiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32icon:$iconFile" "/win32manifest:$(Join-Path $PSScriptRoot 'Launcher/app.manifest')" "/out:$launcherFile" /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'Launcher/Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
Write-Output ('Launcher: ' + $launcherFile)

if ($Package) {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $version = [string]$projectXml.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Invalid package version.' }
    $stagingRoot = Join-Path $projectRoot '.local'
    $staging = Join-Path $stagingRoot ('package-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        Copy-Item -LiteralPath $launcherFile -Destination $staging
        Copy-Item -LiteralPath $appDirectory -Destination $staging -Recurse
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $staging 'README.md')
        $packages = Join-Path $projectRoot 'dist'
        New-Item -ItemType Directory -Path $packages -Force | Out-Null
        $archive = Join-Path $packages ("Transit-Windows-x64-v$version.zip")
        Compress-Archive -LiteralPath @((Join-Path $staging '随译.exe'), (Join-Path $staging 'app'), (Join-Path $staging 'README.md')) -DestinationPath $archive -Force
        Write-Output ('Package: ' + $archive)
    } finally {
        $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path
        $resolvedStagingRoot = (Resolve-Path -LiteralPath $stagingRoot).Path
        if (-not $resolvedStaging.StartsWith($resolvedStagingRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package cleanup path.' }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
