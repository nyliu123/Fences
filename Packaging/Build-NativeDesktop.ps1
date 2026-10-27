param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$VcVarsPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$work = Join-Path $projectRoot ('Fences/obj/NativeDesktop/' + $Configuration)
New-Item -ItemType Directory -Force -Path $work, $output | Out-Null
if (-not $VcVarsPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'Visual Studio C++ x64 Build Tools are required.' }
    $VcVarsPath = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
}
$environmentLines = & $env:ComSpec /d /s /c ('call "' + $VcVarsPath + '" >nul && set')
if ($LASTEXITCODE -ne 0) { throw 'Native build environment initialization failed.' }
$previous = @{}
foreach ($line in $environmentLines) {
    if ($line -match '^([^=]+)=(.*)$') {
        if (-not $previous.ContainsKey($matches[1])) { $previous[$matches[1]] = [Environment]::GetEnvironmentVariable($matches[1], 'Process') }
        [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
    }
}
Push-Location $work
try {
    $options = @('/O2')
    if ($Configuration -eq 'Debug') { $options = @('/Od', '/Zi') }
    & cl.exe /nologo /LD /MT /std:c++17 /EHsc /W4 /utf-8 @options (Join-Path $projectRoot 'Fences.Native/DesktopView.cpp') (Join-Path $projectRoot 'Fences.Native/DesktopMenus.cpp') (Join-Path $projectRoot 'Fences.Native/DesktopSource.cpp') (Join-Path $projectRoot 'Fences.Native/DesktopWallpaper.cpp') (Join-Path $projectRoot 'Fences.Native/RecycleBinIcon.cpp') /Fe:Fences.Desktop.dll /link ole32.lib oleaut32.lib shell32.lib shlwapi.lib uuid.lib user32.lib gdi32.lib gdiplus.lib comctl32.lib uxtheme.lib advapi32.lib /DYNAMICBASE /NXCOMPAT
    if ($LASTEXITCODE -ne 0) { throw 'Fences native desktop build failed.' }
    Copy-Item -LiteralPath (Join-Path $work 'Fences.Desktop.dll') -Destination (Join-Path $output 'Fences.Desktop.dll') -Force
}
finally {
    Pop-Location
    foreach ($name in $previous.Keys) {
        $value = if ($null -eq $previous[$name]) { [NullString]::Value } else { $previous[$name] }
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}
