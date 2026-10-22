param([string]$Generator)
$ErrorActionPreference = 'Stop'
$studioRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Generator) {
    $Generator = if (Test-Path 'C:/Program Files/Microsoft Visual Studio/18') { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
}
$studioOutput = Join-Path $studioRoot 'artifacts/native'
[IO.Directory]::CreateDirectory($studioOutput) | Out-Null
foreach ($studioArchitecture in @('x64','x86')) {
    $studioPlatform = if ($studioArchitecture -eq 'x86') { 'Win32' } else { 'x64' }
    $studioBuild = Join-Path $studioRoot ('artifacts/native-build-' + $studioArchitecture)
    & cmake -S (Join-Path $studioRoot 'native/lua54') -B $studioBuild -G $Generator -A $studioPlatform "-DLUMA_RUNTIME_SUFFIX=$studioArchitecture"
    if ($LASTEXITCODE -ne 0) { throw 'Native Lua configure failed.' }
    & cmake --build $studioBuild --config Release --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Native Lua build failed.' }
    Copy-Item -LiteralPath (Join-Path $studioBuild "Release/lua54-$studioArchitecture.dll") -Destination $studioOutput
}
