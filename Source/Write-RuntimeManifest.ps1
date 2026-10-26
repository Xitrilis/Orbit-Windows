$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$expected = Join-Path $projectRoot 'Notices\playnite-runtime.sha256'
if (-not (Test-Path -LiteralPath $expected)) { throw 'Missing verified Playnite runtime baseline.' }
$officialPaths = @{}
foreach ($line in Get-Content -LiteralPath $expected) {
    $officialPaths['engine/' + $line.Substring(66).Replace('\', '/')] = $true
    $hash = $line.Substring(0, 64)
    $relative = $line.Substring(66).Replace('/', [IO.Path]::DirectorySeparatorChar)
    $file = Join-Path (Join-Path $projectRoot 'engine') $relative
    if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash) {
        throw "Playnite runtime is missing or modified: $relative. Use the complete verified runtime; do not generate a baseline from an incomplete extraction."
    }
}
$rows = New-Object 'System.Collections.Generic.List[string]'
$paths = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'engine') -Recurse -File) + @(Get-Item -LiteralPath (Join-Path $projectRoot 'Orbit.exe.config')) + @(Get-Item -LiteralPath (Join-Path $projectRoot 'orbit.ico'))
foreach ($item in $paths) {
    $relative = $item.FullName.Substring($projectRoot.Length + 1).Replace('\', '/')
    if (-not $officialPaths.ContainsKey($relative) -and ($relative -match '(?i)(^|/)(OrbitData|ExtensionsData|library|cache|logs|webcache|browsercache|Backup|obj|bin)(/|$)' -or $item.Name.StartsWith('.') -or $item.Extension -in @('.pdb', '.log', '.tmp', '.zip') -or $item.Name.StartsWith('Orbit-report-', [StringComparison]::OrdinalIgnoreCase) -or $item.Name -like '*.partial-*' -or $relative -eq 'engine/config.json' -or $relative -eq 'engine/safeStartup.flag')) { continue }
    $rows.Add(((Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()) + "`t" + $item.Length + "`t" + $relative)
}
$sorted = @($rows | Sort-Object)
[IO.File]::WriteAllLines((Join-Path $projectRoot 'RuntimeFiles.sha256'), $sorted, (New-Object Text.UTF8Encoding($false)))
Write-Host ("Runtime manifest: " + $sorted.Length + " files")
