$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginOutput = Join-Path $projectRoot 'engine\Extensions\Orbit'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'For rebuilding, install the .NET 8 SDK. It is not needed to run the supplied Orbit.exe.' }
foreach ($project in @('Orbit.Plugin', 'Orbit.InstallHelper', 'Orbit.Bootstrap')) {
    if ($project -eq 'Orbit.Bootstrap') { & (Join-Path $PSScriptRoot 'Write-RuntimeManifest.ps1') }
    $destination = if ($project -eq 'Orbit.Bootstrap') { $projectRoot } else { $pluginOutput }
    & dotnet build (Join-Path $PSScriptRoot "$project.csproj") -c Release -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
Write-Host 'Built Orbit.exe and the Orbit plugin.'
