param([string]$VcVarsPath)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $projectRoot 'Fences/obj/DesktopViewFilteringResearch'
New-Item -ItemType Directory -Force -Path $output | Out-Null
if (-not $VcVarsPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'Visual Studio C++ Build Tools are required.' }
    $VcVarsPath = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
}
if (-not (Test-Path -LiteralPath $VcVarsPath)) { throw 'vcvars64.bat was not found.' }
$environmentLines = & $env:ComSpec /d /s /c ('call "' + $VcVarsPath + '" >nul && set')
if ($LASTEXITCODE -ne 0) { throw 'C++ build environment initialization failed.' }
$previousEnvironment = @{}
foreach ($line in $environmentLines) {
    if ($line -match '^([^=]+)=(.*)$') {
        if (-not $previousEnvironment.ContainsKey($matches[1])) {
            $previousEnvironment[$matches[1]] = [Environment]::GetEnvironmentVariable($matches[1], 'Process')
        }
        [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
    }
}
Push-Location $output
try {
    & cl.exe /nologo /std:c++17 /EHsc /W4 /utf-8 (Join-Path $PSScriptRoot 'Probe.cpp') (Join-Path $PSScriptRoot 'ContextMenuProbe.cpp') /Fe:DesktopViewFilteringProbe.exe /link ole32.lib oleaut32.lib shell32.lib shlwapi.lib uuid.lib user32.lib gdi32.lib gdiplus.lib comdlg32.lib comctl32.lib
    if ($LASTEXITCODE -ne 0) { throw 'Research probe build failed.' }
    $probeOutput = & ./DesktopViewFilteringProbe.exe $output 2>&1
    $probeExitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllLines((Join-Path $output 'results.txt'), [string[]]$probeOutput, [System.Text.UTF8Encoding]::new($false))
    $probeOutput | Write-Output
    if ($probeExitCode -ne 0) { throw 'Research probe failed. See the results file.' }
}
finally {
    Pop-Location
    foreach ($name in $previousEnvironment.Keys) {
        $value = if ($null -eq $previousEnvironment[$name]) { [NullString]::Value } else { $previousEnvironment[$name] }
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}
