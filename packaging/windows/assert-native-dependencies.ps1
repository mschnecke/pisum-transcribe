#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails when a library in a folder imports a Visual C++ runtime file the folder doesn't contain, or
    when a native library that is loaded by name is missing from it.

.DESCRIPTION
    Reads the import table and the delay-load import table of every .dll and .exe in the folder.
    Every import named like a Visual C++ runtime file (vcruntime*, msvcp*, concrt*, vcomp*,
    vccorlib*) must be a file in that folder, next to the exe, where Windows looks first (design
    D9). A missing one is named together with every file that imports it.

    The development machine and GitHub's runners both have the Visual C++ Redistributable in
    System32, so a missing runtime file would pass every test there and fail only on a clean
    machine. This check stands in for the clean machine in every build.

    Only the VC++ runtime family is checked. The UCRT (api-ms-win-crt-*, ucrtbase.dll) is part of
    Windows 10 and later, vulkan-1.dll comes with the GPU driver and is optional by design, and an
    allowlist of Windows' own DLLs would break with the next Windows version.

    The native libraries of the Avalonia shell (libSkiaSharp.dll, libHarfBuzzSharp.dll and
    av_libglesv2.dll) are loaded by name at run time, so no import table names them. They must be
    files in the folder too. Their own imports go through the same scan.

    The PE reader is here rather than dumpbin, which exists only where Visual Studio does, or
    objdump, which is neither on the runner nor in Git for Windows.

.PARAMETER Path
    The folder to check, such as the published 'Pisum Transcribe' folder or a build output folder.

.PARAMETER ListMissing
    Writes the names of the missing runtime files to the pipeline and succeeds, instead of failing.
    build-msi.ps1 uses it to find the files to copy with the same scan that later checks them.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [switch] $ListMissing
)

$ErrorActionPreference = 'Stop'

$runtimePattern = '^(vcruntime|msvcp|concrt|vcomp|vccorlib)'

# Loaded by name at run time: SkiaSharp and HarfBuzzSharp by P/Invoke, ANGLE by Avalonia.Win32.
$loadedByName = 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'av_libglesv2.dll'

# The names of the DLLs a PE file imports, from its import directory (entry 1) and its delay-load
# import directory (entry 13). Returns $null for a file that isn't a PE image.
function Get-PeImports([string] $file) {
    $stream = [System.IO.File]::OpenRead($file)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)

        if ($stream.Length -lt 64) { return $null }
        if ($reader.ReadUInt16() -ne 0x5A4D) { return $null }   # 'MZ'
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        if ($peOffset + 24 -gt $stream.Length) { return $null }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { return $null }   # 'PE\0\0'

        # COFF file header
        $null = $reader.ReadUInt16()   # Machine
        $sectionCount = $reader.ReadUInt16()
        $stream.Position += 12         # TimeDateStamp, PointerToSymbolTable, NumberOfSymbols
        $optionalHeaderSize = $reader.ReadUInt16()
        $null = $reader.ReadUInt16()   # Characteristics

        $optionalHeader = $stream.Position
        $magic = $reader.ReadUInt16()
        switch ($magic) {
            0x10B { $stream.Position = $optionalHeader + 28; $imageBase = [uint64]$reader.ReadUInt32(); $directories = $optionalHeader + 96 }
            0x20B { $stream.Position = $optionalHeader + 24; $imageBase = $reader.ReadUInt64(); $directories = $optionalHeader + 112 }
            default { return $null }
        }
        $stream.Position = $directories - 4
        $directoryCount = $reader.ReadUInt32()

        $sections = for ($i = 0; $i -lt $sectionCount; $i++) {
            $stream.Position = $optionalHeader + $optionalHeaderSize + 40 * $i + 8
            $virtualSize = $reader.ReadUInt32()
            $virtualAddress = $reader.ReadUInt32()
            $rawSize = $reader.ReadUInt32()
            $rawPointer = $reader.ReadUInt32()
            [pscustomobject]@{ Start = $virtualAddress; End = $virtualAddress + [math]::Max($virtualSize, $rawSize); Raw = $rawPointer }
        }

        $toOffset = {
            param([uint64] $rva)
            foreach ($s in $sections) {
                if ($rva -ge $s.Start -and $rva -lt $s.End) { return [int64]($rva - $s.Start + $s.Raw) }
            }
            return -1
        }
        $readName = {
            param([uint64] $rva)
            $offset = & $toOffset $rva
            if ($offset -lt 0) { return $null }
            $stream.Position = $offset
            $bytes = [System.Collections.Generic.List[byte]]::new()
            while (($b = $stream.ReadByte()) -gt 0) { $bytes.Add([byte]$b) }
            return [System.Text.Encoding]::ASCII.GetString($bytes.ToArray())
        }
        $directoryRva = {
            param([int] $index)
            if ($index -ge $directoryCount) { return 0 }
            $stream.Position = $directories + 8 * $index
            return $reader.ReadUInt32()
        }

        $names = [System.Collections.Generic.List[string]]::new()

        # IMAGE_IMPORT_DESCRIPTOR: 20 bytes, the DLL name's RVA at +12, ended by an all-zero entry.
        $rva = & $directoryRva 1
        if ($rva -ne 0) {
            $entry = & $toOffset $rva
            for (; $entry -ge 0; $entry += 20) {
                $stream.Position = $entry
                $originalFirstThunk = $reader.ReadUInt32()
                $stream.Position = $entry + 12
                $nameRva = $reader.ReadUInt32()
                $firstThunk = $reader.ReadUInt32()
                if ($originalFirstThunk -eq 0 -and $nameRva -eq 0 -and $firstThunk -eq 0) { break }
                $name = & $readName $nameRva
                if ($name) { $names.Add($name) }
            }
        }

        # IMAGE_DELAYLOAD_DESCRIPTOR: 32 bytes, attributes at +0 and the DLL name at +4, ended by an
        # all-zero entry. Without the RVA attribute (bit 0), the name is a VA from the old VC6
        # format, relative to the image base.
        $rva = & $directoryRva 13
        if ($rva -ne 0) {
            $entry = & $toOffset $rva
            for (; $entry -ge 0; $entry += 32) {
                $stream.Position = $entry
                $attributes = $reader.ReadUInt32()
                $nameRva = [uint64]$reader.ReadUInt32()
                if ($attributes -eq 0 -and $nameRva -eq 0) { break }
                if (($attributes -band 1) -eq 0) { $nameRva -= $imageBase }
                $name = & $readName $nameRva
                if ($name) { $names.Add($name) }
            }
        }

        return , $names.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

$folder = (Resolve-Path -LiteralPath $Path).ProviderPath
$files = Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Extension -in '.dll', '.exe' }
$present = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
Get-ChildItem -LiteralPath $folder -File | ForEach-Object { $null = $present.Add($_.Name) }

# Runtime file name (lower case) -> the files that import it.
$importers = [ordered]@{}
$scanned = 0
foreach ($file in $files) {
    $imports = Get-PeImports $file.FullName
    if ($null -eq $imports) { continue }
    $scanned++
    foreach ($import in $imports | Sort-Object -Unique) {
        if ($import -notmatch $runtimePattern) { continue }
        $key = $import.ToLowerInvariant()
        if (-not $importers.Contains($key)) { $importers[$key] = [System.Collections.Generic.List[string]]::new() }
        $importers[$key].Add($file.Name)
    }
}

Write-Host "Checked the imports of $scanned PE files in $folder for Visual C++ runtime files."
$missing = @()
foreach ($name in $importers.Keys | Sort-Object) {
    $users = ($importers[$name] | Sort-Object) -join ', '
    if ($present.Contains($name)) {
        Write-Host "  ok       $name  <- $users"
    }
    else {
        Write-Host "  MISSING  $name  <- $users"
        $missing += $name
    }
}

if ($ListMissing) {
    $missing
    exit 0
}

Write-Host "Checked the native libraries that are loaded by name."
$absent = @()
foreach ($name in $loadedByName) {
    if ($present.Contains($name)) {
        Write-Host "  ok       $name"
    }
    else {
        Write-Host "  MISSING  $name"
        $absent += $name
    }
}

if ($missing.Count -gt 0) {
    Write-Host "Missing Visual C++ runtime files: $($missing -join ', '). A machine without the Visual C++ Redistributable can't load the files that import them."
}
if ($absent.Count -gt 0) {
    Write-Host "Missing native libraries: $($absent -join ', '). The app can't show its windows or its tray without them."
}
if ($missing.Count -gt 0 -or $absent.Count -gt 0) {
    exit 1
}
Write-Host "All Visual C++ runtime imports and native libraries are present."
# Explicit, so a caller that checks $LASTEXITCODE never sees a value left over from an earlier command.
exit 0
