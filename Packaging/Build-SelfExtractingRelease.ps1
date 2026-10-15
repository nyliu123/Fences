param(
    [Parameter(Mandatory = $true)]
    [string]$SevenZipDirectory,
    [string]$MsBuildPath = 'msbuild.exe'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sevenZip = Join-Path $SevenZipDirectory '7z.exe'
$module = Join-Path $SevenZipDirectory '7z.sfx'
$vendorLicense = Join-Path $SevenZipDirectory 'License.txt'
foreach ($path in @($sevenZip, $module, $vendorLicense)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing 7-Zip component: $path" }
}

& $MsBuildPath (Join-Path $projectRoot 'Fences.sln') /t:Build /p:Configuration=Release /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Fences Release build failed.' }

$releaseDirectory = Join-Path $projectRoot 'Fences/bin/Release'
$version = [version][System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $releaseDirectory 'Fences.exe')).FileVersion
$fileName = 'Fences-' + $version.ToString(3) + '-win-x64-sfx.exe'
$staging = Join-Path $projectRoot ('Fences/obj/SelfExtractingRelease/' + [guid]::NewGuid().ToString('N'))
$outputDirectory = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $staging -Force | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

# Explicit files only: never enumerate or package user data, Logs, Data, or AppData.
$binaries = @(
    'Fences.exe', 'Fences.exe.config', 'Fences.Recovery.exe', 'Fences.Recovery.exe.config',
    'Fences.pdb', 'cs/Fences.resources.dll', 'zh-CN/Fences.resources.dll'
)
$documents = @('README.md', 'LICENSE', 'DESIGN.md', 'Tests/README.md', 'Packaging/README.md')
foreach ($relative in $binaries + $documents) {
    $sourceRoot = if ($relative -in $binaries) { $releaseDirectory } else { $projectRoot }
    $source = Join-Path $sourceRoot $relative
    $destination = Join-Path $staging $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release file: $source" }
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

# These are runtime defaults from source, not existing per-user configuration files.
$defaultConfigHash = (Get-FileHash -LiteralPath (Join-Path $projectRoot 'Fences/App.config') -Algorithm SHA256).Hash
foreach ($relative in @('Fences.exe.config', 'Fences.Recovery.exe.config')) {
    if ((Get-FileHash -LiteralPath (Join-Path $staging $relative) -Algorithm SHA256).Hash -ne $defaultConfigHash) {
        throw "Runtime config differs from the source defaults: $relative"
    }
}

$licensePath = 'ThirdParty/7-Zip/LICENSE.txt'
$lgplPath = 'ThirdParty/7-Zip/COPYING.LGPL-2.1.txt'
$noticePath = 'ThirdParty/7-Zip/NOTICE.txt'
New-Item -ItemType Directory -Path (Join-Path $staging 'ThirdParty/7-Zip') -Force | Out-Null
Copy-Item -LiteralPath $vendorLicense -Destination (Join-Path $staging $licensePath)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Licenses/LGPL-2.1.txt') -Destination (Join-Path $staging $lgplPath)
$toolVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($sevenZip).FileVersion
@"
This self-extracting archive uses the unmodified official 7-Zip Windows SFX module.
7-Zip version: $toolVersion
Copyright (C) 1999-2026 Igor Pavlov.
License information: LICENSE.txt in this directory.
Full LGPL 2.1 text: COPYING.LGPL-2.1.txt in this directory.
Official project and corresponding source downloads:
https://www.7-zip.org/download.html
https://github.com/ip7z/7zip
The SFX module is prepended to the archive; it can be replaced independently.
Fences itself retains its separate MIT license in the package root.
"@ | Set-Content -LiteralPath (Join-Path $staging $noticePath) -Encoding utf8

$payload = @($binaries + $documents + @($licensePath, $lgplPath, $noticePath))
$temporaryOutput = Join-Path (Split-Path $staging) ([guid]::NewGuid().ToString('N') + '.exe')
Push-Location $staging
try {
    & $sevenZip a -t7z -mx=9 "-sfx$module" $temporaryOutput @payload
    if ($LASTEXITCODE -ne 0) { throw 'Self-extracting archive creation failed.' }
    & $sevenZip t $temporaryOutput
    if ($LASTEXITCODE -ne 0) { throw 'Self-extracting archive integrity check failed.' }
}
finally { Pop-Location }

$output = Join-Path $outputDirectory $fileName
Move-Item -LiteralPath $temporaryOutput -Destination $output -Force
Write-Output ('Self-extracting release: ' + $output)
Write-Output ('Staged payload: ' + $staging)
