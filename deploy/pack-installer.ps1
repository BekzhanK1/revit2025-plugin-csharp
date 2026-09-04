# Builds Release plugin and Setup.exe (Inno Setup 6).
# From repo root:
#   powershell -ExecutionPolicy Bypass -File deploy\pack-installer.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $root "SBS\bin\Release\net8.0-windows"
$payload = Join-Path $PSScriptRoot "payload"
$pluginOut = Join-Path $payload "SmartRemont"
$iss = Join-Path $PSScriptRoot "SmartRemont.ExportRooms.iss"
$addin = Join-Path $PSScriptRoot "SmartRemont.ExportRooms.addin"

function New-IcoFromPng {
    param(
        [string]$PngPath,
        [string]$IcoPath,
        [int[]]$Sizes = @(16, 32, 48)
    )
    Add-Type -AssemblyName System.Drawing
    $src = $null
    $fs = [System.IO.File]::OpenRead($PngPath)
    try {
        $src = New-Object System.Drawing.Bitmap $fs
        $images = New-Object System.Collections.Generic.List[byte[]]
        $dims = New-Object System.Collections.Generic.List[int]
        foreach ($size in $Sizes) {
            $bmp = New-Object System.Drawing.Bitmap $size, $size
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.DrawImage($src, 0, 0, $size, $size)
            $g.Dispose()
            $ms = New-Object System.IO.MemoryStream
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
            $images.Add($ms.ToArray())
            $dims.Add($size)
            $ms.Dispose()
        }
    }
    finally {
        if ($src) { $src.Dispose() }
        $fs.Dispose()
    }

    $count = $images.Count
    $offset = 6 + (16 * $count)
    $msOut = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $msOut
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$count)
    foreach ($i in 0..($count - 1)) {
        $s = $dims[$i]
        $w = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$w)
        $bw.Write([byte]$w)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$images[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $bw.Write($bytes) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($IcoPath, $msOut.ToArray())
    $bw.Dispose()
    $msOut.Dispose()
}

function Find-Iscc {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

Write-Host "Build plugin Release..."
dotnet build (Join-Path $root "SBS.sln") -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $bin "SmartRemont.ExportRooms.dll"
if (-not (Test-Path $dll)) {
    throw "DLL not found: $dll"
}

if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $pluginOut | Out-Null

Get-ChildItem $bin -File | Where-Object {
    $_.Extension -in ".dll", ".json", ".config" -and
    $_.Name -notlike "RevitAPI*" -and
    $_.Name -notlike "*.xml"
} | Copy-Item -Destination $pluginOut

$resDst = Join-Path $pluginOut "Resources"
$resSources = @(
    (Join-Path $root "SBS\Resources"),
    (Join-Path $root "SmartRemont.ExportSpecifications\Resources")
)
foreach ($resSrc in $resSources) {
    if (-not (Test-Path $resSrc)) { continue }
    $png = @(Get-ChildItem $resSrc -File -Filter "*.png" -ErrorAction SilentlyContinue)
    if ($png.Count -eq 0) { continue }
    New-Item -ItemType Directory -Path $resDst -Force | Out-Null
    foreach ($f in $png) {
        $dest = Join-Path $resDst $f.Name
        if (-not (Test-Path $dest)) {
            Copy-Item -LiteralPath $f.FullName -Destination $dest
        }
    }
}

Copy-Item $addin (Join-Path $payload "SmartRemont.ExportRooms.addin")

$iconPng = Join-Path $pluginOut "Resources\export_32.png"
if (-not (Test-Path $iconPng)) {
    throw "Ribbon icon not found: $iconPng"
}
New-IcoFromPng -PngPath $iconPng -IcoPath (Join-Path $payload "smartremont.ico")

$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host "Inno Setup 6 not found. Installing via winget..."
    winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
    $iscc = Find-Iscc
}
if (-not $iscc) {
    throw "Install Inno Setup 6 and retry: https://jrsoftware.org/isinfo.php"
}

Write-Host "Compile installer: $iscc"
& $iscc $iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Join-Path $PSScriptRoot "out\SmartRemont-Revit-2025-Setup.exe"
if (-not (Test-Path $setup)) { throw "Setup.exe not found: $setup" }
Write-Host "OK: $setup"
