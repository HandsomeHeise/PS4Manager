param([Parameter(Mandatory=$true)][string]$FixtureRoot)
$ErrorActionPreference = 'Stop'
$packageScript = Join-Path $PSScriptRoot 'package-client.ps1'
$build = Join-Path $FixtureRoot 'build'
[IO.Directory]::CreateDirectory($build) | Out-Null
$names = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'client-runtime.json') -Raw | ConvertFrom-Json)
function Expect-Failure([scriptblock]$Action, [string]$Pattern) {
    try { & $Action; throw 'Expected a packaging failure.' }
    catch { if ($_.Exception.Message -notmatch $Pattern) { throw } }
}
Expect-Failure { & $packageScript -OutputPath (Join-Path $FixtureRoot 'missing.zip') -BuildDirectory $build } 'Missing runtime file'
foreach($name in $names) { [IO.File]::WriteAllText((Join-Path $build $name),('Fixture content for ' + $name)) }
[IO.File]::WriteAllText((Join-Path $build 'credentials.json'),'do not package')
[IO.File]::WriteAllText((Join-Path $build 'Private.Tests.dll'),'do not package')
$output = Join-Path $FixtureRoot 'verified.zip'
& $packageScript -OutputPath $output -BuildDirectory $build
$before = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
Expect-Failure { & $packageScript -OutputPath $output -BuildDirectory $build } 'Output already exists'
if ((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash -ne $before) { throw 'Existing ZIP was modified.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($output)
try {
    if($archive.Entries.Count -ne $names.Count) { throw 'Unexpected package entries.' }
    foreach($entry in $archive.Entries) { if($names -notcontains $entry.FullName) { throw 'Unexpected file packaged.' } }
} finally { $archive.Dispose() }
$unlocked = [IO.File]::Open($output,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
$unlocked.Dispose()
'PASS: missing input, exclusive output, runtime manifest allowlist, SHA256 verification, closed archive handles.'
