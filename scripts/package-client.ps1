param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '../src/PS4Manager.App/bin/Release/net48')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$destination = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $destination) { throw 'Output already exists.' }
$build = [IO.Path]::GetFullPath($BuildDirectory)
# Explicit runtime allowlist prevents test DLLs, user settings and credentials from leaking into a release.
$names = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'client-runtime.json') -Raw | ConvertFrom-Json)
if ($names.Count -eq 0 -or ($names | Select-Object -Unique).Count -ne $names.Count) { throw 'Invalid runtime manifest.' }
foreach ($name in $names) {
    if ($name -notmatch '\A(?:(?!\.\.?$)[A-Za-z0-9_.-]+|runtimes/win-(?:x86|x64)/native/(?:Lexilla|Scintilla)\.dll)\z' -or $name -match '^(UIFramework|.*Tests\.)') { throw 'Invalid runtime manifest entry.' }
}
foreach ($name in $names) {
    if (-not [IO.File]::Exists((Join-Path $build $name))) { throw "Missing runtime file: $name" }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
$stream = [IO.File]::Open($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        foreach ($name in $names) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $build $name),$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($destination)
try {
    if ($archive.Entries.Count -ne $names.Count) { throw 'ZIP entry count mismatch.' }
    foreach ($entry in $archive.Entries) {
        if ($names -notcontains $entry.FullName) { throw 'Unexpected ZIP entry.' }
        $source = Get-Item -LiteralPath (Join-Path $build $entry.FullName)
        if ($entry.Length -ne $source.Length) { throw "ZIP size mismatch: $($entry.FullName)" }
        $inputStream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($inputStream)).Replace('-','') }
        finally { $sha.Dispose(); $inputStream.Dispose() }
        if ($actual -ne (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash) { throw "ZIP hash mismatch: $($entry.FullName)" }
    }
} finally { $archive.Dispose() }
[pscustomobject]@{ Path=$destination; Files=$names.Count; SHA256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
