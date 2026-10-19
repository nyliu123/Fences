param(
    [Parameter(Mandatory = $true)]
    [string]$SevenZipSourceDirectory,
    [string]$DefaultDirectory = 'C:\Program Files\Fences',
    [string]$VcVarsPath,
    [ValidateSet('requireAdministrator', 'asInvoker')]
    [string]$ExecutionLevel = 'requireAdministrator'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceRoot = [System.IO.Path]::GetFullPath($SevenZipSourceDirectory)
$sourceFile = Join-Path $sourceRoot 'CPP/7zip/Bundles/SFXWin/SfxWin.cpp'
if (-not (Test-Path -LiteralPath $sourceFile)) { throw 'The full official 7-Zip source tree is required.' }
if (-not [System.IO.Path]::IsPathRooted($DefaultDirectory) -or $DefaultDirectory.Contains('"')) {
    throw 'The default extraction directory must be an absolute Windows directory without quotes.'
}
if (-not $VcVarsPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'Visual Studio C++ Build Tools are required to build the SFX module.' }
    $VcVarsPath = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
}
if (-not (Test-Path -LiteralPath $VcVarsPath)) { throw 'vcvars64.bat was not found.' }

$moduleDirectory = Join-Path $projectRoot 'Fences/obj/SfxModule'
$work = Join-Path $moduleDirectory ([guid]::NewGuid().ToString('N'))
$sourceCopy = Join-Path $work 'source'
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item -LiteralPath $sourceRoot -Destination $sourceCopy -Recurse
$cpp = Join-Path $sourceCopy 'CPP/7zip/Bundles/SFXWin/SfxWin.cpp'
(Get-Item -LiteralPath $cpp).IsReadOnly = $false
$text = [System.IO.File]::ReadAllText($cpp)
$original = 'eo.OutputDir = outputFolderDefined ? outputFolder : dirPrefix;'
if (-not $text.Contains($original)) { throw 'Unsupported 7-Zip source layout: default output assignment not found.' }
$escapedDirectory = $DefaultDirectory.TrimEnd('\').Replace('\', '\\') + '\\'
$replacement = 'eo.OutputDir = outputFolderDefined ? outputFolder : FString(L"' + $escapedDirectory + '");'
[System.IO.File]::WriteAllText($cpp, $text.Replace($original, $replacement), [System.Text.UTF8Encoding]::new($false))

$manifestPath = Join-Path $sourceCopy 'CPP/7zip/UI/GUI/7zG.exe.manifest'
(Get-Item -LiteralPath $manifestPath).IsReadOnly = $false
$manifest = [xml][System.IO.File]::ReadAllText($manifestPath)
$namespace = 'urn:schemas-microsoft-com:asm.v3'
$trust = $manifest.CreateElement('trustInfo', $namespace)
$security = $manifest.CreateElement('security', $namespace)
$privileges = $manifest.CreateElement('requestedPrivileges', $namespace)
$level = $manifest.CreateElement('requestedExecutionLevel', $namespace)
$level.SetAttribute('level', $ExecutionLevel)
$level.SetAttribute('uiAccess', 'false')
[void]$privileges.AppendChild($level)
[void]$security.AppendChild($privileges)
[void]$trust.AppendChild($security)
[void]$manifest.DocumentElement.AppendChild($trust)
[System.IO.File]::WriteAllText($manifestPath, $manifest.OuterXml, [System.Text.UTF8Encoding]::new($false))

# Import the compiler environment only into this build process.
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
$bundle = Join-Path $sourceCopy 'CPP/7zip/Bundles/SFXWin'
Push-Location $bundle
try {
    & nmake.exe /nologo PLATFORM=x64 CFLAGS_WARN_LEVEL=-W3
    if ($LASTEXITCODE -ne 0) { throw 'Native SFX module compilation failed.' }
}
finally {
    Pop-Location
    foreach ($name in $previousEnvironment.Keys) {
        $value = if ($null -eq $previousEnvironment[$name]) { [NullString]::Value } else { $previousEnvironment[$name] }
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}
$name = if ($ExecutionLevel -eq 'requireAdministrator') { '7z.sfx' } else { '7z-test.sfx' }
$module = Join-Path $moduleDirectory $name
Copy-Item -LiteralPath (Join-Path $bundle 'x64/7z.sfx') -Destination $module -Force
Copy-Item -LiteralPath $manifestPath -Destination ($module + '.manifest') -Force
Write-Output ('Built native SFX module: ' + $module)
Write-Output ('Default directory: ' + $DefaultDirectory)
Write-Output ('Execution level: ' + $ExecutionLevel)
