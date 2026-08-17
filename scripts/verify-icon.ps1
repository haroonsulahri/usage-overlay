$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $projectRoot 'src\CodexUsageOverlay\Assets\CodexUsageOverlay.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "Icon file not found: $iconPath"
}

$requiredSizes = @(16, 24, 32, 48, 64, 128, 256)
$stream = [System.IO.File]::OpenRead($iconPath)
$reader = New-Object System.IO.BinaryReader $stream

try {
    $reserved = $reader.ReadUInt16()
    $type = $reader.ReadUInt16()
    $count = $reader.ReadUInt16()
    if ($reserved -ne 0 -or $type -ne 1 -or $count -ne $requiredSizes.Count) {
        throw "Invalid ICO header: reserved=$reserved type=$type count=$count"
    }

    $actualSizes = @()
    for ($index = 0; $index -lt $count; $index++) {
        $widthByte = $reader.ReadByte()
        $heightByte = $reader.ReadByte()
        [void]$reader.ReadByte()
        [void]$reader.ReadByte()
        $planes = $reader.ReadUInt16()
        $bitCount = $reader.ReadUInt16()
        $byteCount = $reader.ReadUInt32()
        $offset = $reader.ReadUInt32()
        $width = if ($widthByte -eq 0) { 256 } else { [int]$widthByte }
        $height = if ($heightByte -eq 0) { 256 } else { [int]$heightByte }

        if ($width -ne $height -or $planes -ne 1 -or $bitCount -ne 32) {
            throw "Invalid ICO entry for ${width}x${height}: planes=$planes bitCount=$bitCount"
        }
        if ($offset + $byteCount -gt $stream.Length) {
            throw "ICO entry ${width}px extends beyond the file."
        }
        $actualSizes += $width
    }

    if (($actualSizes -join ',') -ne ($requiredSizes -join ',')) {
        throw "Unexpected icon sizes: $($actualSizes -join ', ')"
    }
}
finally {
    $reader.Dispose()
    $stream.Dispose()
}

Write-Host "Icon verification passed: $($requiredSizes -join ', ') px"

