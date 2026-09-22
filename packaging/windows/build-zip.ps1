#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the release zip.

.DESCRIPTION
    Publishes win-x64 self-contained and zips the 'Pisum Transcribe' folder into
    artifacts\Pisum.Transcribe_<version>_win-x64.zip. The one command that turns a clean checkout
    into the release zip - the same command a person and both workflows run (design D3).

    Needs PowerShell 7. On a machine without Visual Studio, the Visual C++ runtime is copied from
    System32 with a warning; in CI ($env:CI is 'true') that is an error.

.PARAMETER Version
    The release version, without a leading 'v'. May carry a pre-release suffix (0.1.0-rc.1).
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$root = (Resolve-Path (Join-Path $scriptDir '..' '..')).ProviderPath
$guard = Join-Path $scriptDir 'assert-native-dependencies.ps1'

# The payload is published beside this script, and packaging/windows/.gitignore keeps it untracked.
$publishDir = Join-Path $scriptDir 'publish'
$appDir = Join-Path $publishDir 'Pisum Transcribe'
$outputDir = Join-Path $root 'artifacts'
$zip = Join-Path $outputDir "Pisum.Transcribe_${Version}_win-x64.zip"

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $zip) { Remove-Item -Force $zip }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# 1. Self-contained and ReadyToRun; not single-file and not trimmed (design D2). The native
# package ships three license files that NuGet flattens to one LICENSE; build lets the last one
# win, publish refuses (NETSDK1152) unless told not to. Step 3 overwrites that LICENSE anyway, and
# the three texts are in THIRD-PARTY-NOTICES.md.
dotnet publish (Join-Path $root 'src' 'Pisum.Transcribe') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:PublishDocumentationFile=false `
    -p:ErrorOnDuplicatePublishOutputFiles=false `
    -p:Version=$Version `
    --output $appDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# 2. Import libraries, needed only to link against onnxruntime.dll, never at run time.
Get-ChildItem -LiteralPath $appDir -Filter '*.lib' -File | Remove-Item -Force

# 3. The project's license, over the flattened one from the native package.
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $appDir 'LICENSE') -Force

# 4. The notices (design D5).
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $appDir
Copy-Item -LiteralPath (Join-Path $root 'packaging' 'third-party' 'onnxruntime-ThirdPartyNotices.txt') `
    -Destination (Join-Path $appDir 'ThirdPartyNotices-OnnxRuntime.txt')

# 5. The Visual C++ runtime next to the exe, "local deployment" (design D9). The source is the
# newest VC\Redist\MSVC\<version>\x64\Microsoft.VC14*.CRT of any Visual Studio instance - all of
# them, since -latest can pick one without the C++ tools - and a runtime file outside the CRT
# folder, such as vcomp140.dll, comes from its sibling folders of the same version.
$sourceDirs = @()
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio' 'Installer' 'vswhere.exe'
if (Test-Path $vswhere) {
    $crt = & $vswhere -all -products * -prerelease -property installationPath |
        ForEach-Object { Get-ChildItem -Path (Join-Path $_ 'VC' 'Redist' 'MSVC') -Directory -ErrorAction SilentlyContinue } |
        Where-Object { $_.Name -as [version] } |
        ForEach-Object { Get-ChildItem -Path (Join-Path $_.FullName 'x64') -Directory -Filter 'Microsoft.VC14*.CRT' -ErrorAction SilentlyContinue } |
        Sort-Object { [version]$_.Parent.Parent.Name }, Name |
        Select-Object -Last 1
    if ($crt) {
        $sourceDirs = @($crt.FullName) + @(Get-ChildItem -Path $crt.Parent.FullName -Directory -Filter 'Microsoft.VC14*' |
            Where-Object { $_.FullName -ne $crt.FullName } | ForEach-Object FullName)
    }
}
if (-not $sourceDirs) {
    # A published release takes the files from a redist folder, which the Visual Studio license
    # terms cover for redistribution. System32 is for a zip a person builds to try out.
    if ($env:CI -eq 'true') { throw 'No Visual Studio Microsoft.VC14*.CRT folder found. A release must not take the Visual C++ runtime from System32.' }
    Write-Warning "No Visual Studio Microsoft.VC14*.CRT folder found. Copying the Visual C++ runtime from System32; don't publish this zip."
    $sourceDirs = @(Join-Path $env:WINDIR 'System32')
}
Write-Host "Visual C++ runtime source: $($sourceDirs[0])"

# The same scan as the guard in step 6, repeated until nothing is missing, because the copied files
# import each other (msvcp140.dll imports vcruntime140.dll).
$copied = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
while ($missing = @(& $guard -Path $appDir -ListMissing 6>$null)) {
    if ($LASTEXITCODE -ne 0) { throw "$guard failed with exit code $LASTEXITCODE" }
    foreach ($name in $missing) {
        if (-not $copied.Add($name)) { throw "$name is still missing after it was copied." }
        $source = $sourceDirs | ForEach-Object { Join-Path $_ $name } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $source) { throw "$name is imported by the payload but not in $($sourceDirs -join ', ')." }
        Copy-Item -LiteralPath $source -Destination $appDir
        Write-Host "  copied $name $((Get-Item $source).VersionInfo.FileVersion) from $(Split-Path $source)"
    }
}

# 6. The guard: every Visual C++ runtime import is in the folder.
& $guard -Path $appDir
if ($LASTEXITCODE -ne 0) { throw "$guard failed with exit code $LASTEXITCODE" }

# 7. The folder itself, not its contents, so extracting gives one 'Pisum Transcribe' folder.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($appDir, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

$payload = Get-ChildItem -Recurse -File $appDir | Measure-Object -Property Length -Sum
Write-Host "Created: $zip"
Write-Host "  Version:  $Version"
Write-Host "  Payload:  $([math]::Round($payload.Sum / 1MB, 1)) MB in $($payload.Count) files"
Write-Host "  Zip size: $([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB"
