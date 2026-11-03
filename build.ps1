$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location -LiteralPath $root

$mascotPng = Join-Path $root "assets\meinya_mascot.png"
$icoPath = Join-Path $root "assets\app.ico"

# 1. Generate multi-resolution app.ico (16, 32, 48, 64, 256) from assets\meinya_mascot.png
if ((Test-Path -LiteralPath $mascotPng) -and -not (Test-Path -LiteralPath $icoPath)) {
    Add-Type -AssemblyName System.Drawing
    $srcBmp = [System.Drawing.Bitmap]::FromFile($mascotPng)
    try {
        $sizes = @(16, 32, 48, 64, 256)
        $pngBuffers = New-Object System.Collections.Generic.List[byte[]]
        foreach ($sz in $sizes) {
            $resized = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($resized)
            try {
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.DrawImage($srcBmp, 0, 0, $sz, $sz)
                $ms = New-Object System.IO.MemoryStream
                $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                $pngBuffers.Add($ms.ToArray())
                $ms.Dispose()
            }
            finally {
                $g.Dispose()
                $resized.Dispose()
            }
        }

        $fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
        $bw = New-Object System.IO.BinaryWriter($fs)
        try {
            $bw.Write([uint16]0)
            $bw.Write([uint16]1)
            $bw.Write([uint16]$sizes.Count)

            $offset = 6 + (16 * $sizes.Count)
            for ($i = 0; $i -lt $sizes.Count; $i++) {
                $sz = $sizes[$i]
                $buf = $pngBuffers[$i]
                $wByte = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
                $bw.Write($wByte)
                $bw.Write($wByte)
                $bw.Write([byte]0)
                $bw.Write([byte]0)
                $bw.Write([uint16]1)
                $bw.Write([uint16]32)
                $bw.Write([uint32]$buf.Length)
                $bw.Write([uint32]$offset)
                $offset += $buf.Length
            }

            for ($i = 0; $i -lt $sizes.Count; $i++) {
                $bw.Write($pngBuffers[$i])
            }
        }
        finally {
            $bw.Dispose()
            $fs.Dispose()
        }
        Write-Host "[BUILD] Generated multi-size icon: $icoPath"
    }
    finally {
        $srcBmp.Dispose()
    }
}

# 2. Remove temporary brief/log files if present
foreach ($tmpName in @("IMAGE_BRIEF.md", "image_gen_result.md")) {
    $tmpFile = Join-Path $root ("assets\" + $tmpName)
    if (Test-Path -LiteralPath $tmpFile) {
        Remove-Item -LiteralPath $tmpFile -Force
    }
}

# 3. Compile C# sources with .NET Framework csc.exe
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "Cannot find C# compiler at $csc"
}

$outExe = Join-Path $root "MeinyaTabletBooster.exe"
Stop-Process -Name "MeinyaTabletBooster" -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $outExe) {
    try {
        Remove-Item -LiteralPath $outExe -Force -ErrorAction Stop
    }
    catch {
        $oldExe = Join-Path $root ("MeinyaTabletBooster.old." + [Environment]::TickCount + ".exe")
        Move-Item -LiteralPath $outExe -Destination $oldExe -Force -ErrorAction SilentlyContinue
    }
}
Get-ChildItem -LiteralPath $root -Filter "MeinyaTabletBooster.old.*.exe" -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
}
$srcFiles = @(
    (Join-Path $root "src\Program.cs"),
    (Join-Path $root "src\ModelsAndDatabase.cs"),
    (Join-Path $root "src\ResamplerAndMemory.cs"),
    (Join-Path $root "src\WacomConfigManager.cs"),
    (Join-Path $root "src\MainForm.cs")
)

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    ("/out:" + $outExe),
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Web.Extensions.dll"
)
if (Test-Path -LiteralPath $icoPath) {
    $cscArgs += ("/win32icon:" + $icoPath)
}
$cscArgs += $srcFiles

Write-Host "[BUILD] Compiling MeinyaTabletBooster.exe..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}
Write-Host "[BUILD] Compiled successfully: $outExe"

# 4. Run Automated Self-Test & UI Smoke Test (pipe stdout so PowerShell waits for winexe and checks exit code)
Write-Host "[TEST] Running --self-test..."
& $outExe --self-test 2>&1 | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "--self-test failed with exit code $LASTEXITCODE"
}

Write-Host "[TEST] Running --ui-smoke-test..."
& $outExe --ui-smoke-test 2>&1 | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "--ui-smoke-test failed with exit code $LASTEXITCODE"
}

Write-Host "[DONE] Build and all automated verification gates PASSED!"
