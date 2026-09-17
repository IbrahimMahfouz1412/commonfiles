# Split/join round-trip tests for FileSplitter.exe.
# Usage: powershell -ExecutionPolicy Bypass -File test-cli.ps1 [-Exe path\to\FileSplitter.exe]
param([string]$Exe = (Join-Path $PSScriptRoot "FileSplitter.exe"))

$ErrorActionPreference = "Stop"
$Exe = (Resolve-Path $Exe).Path
$work = Join-Path $env:TEMP ("fs_test_" + [guid]::NewGuid())
New-Item -ItemType Directory $work | Out-Null
$failed = 0

function Run([string[]]$argList) {
    $p = Start-Process -FilePath $Exe -ArgumentList $argList -Wait -PassThru -WindowStyle Hidden
    return $p.ExitCode
}

function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS  $name" -ForegroundColor Green }
    else { Write-Host "FAIL  $name" -ForegroundColor Red; $script:failed++ }
}

$cases = @(
    @{ Bytes = 10000123; Max = "1MB";  Parts = 10 },
    @{ Bytes = 3145728;  Max = "1MB";  Parts = 3 },
    @{ Bytes = 5;        Max = "1KB";  Parts = 1 },
    @{ Bytes = 0;        Max = "1KB";  Parts = 1 },
    @{ Bytes = 52428800; Max = "7.5MB"; Parts = 7 }
)

foreach ($c in $cases) {
    $label = "$($c.Bytes) bytes / $($c.Max)"
    $dir = Join-Path $work ("case_" + $c.Bytes)
    New-Item -ItemType Directory $dir | Out-Null
    $src = Join-Path $dir "sample data.bin"   # space in name on purpose
    $bytes = New-Object byte[] $c.Bytes
    (New-Object Random 42).NextBytes($bytes)
    [IO.File]::WriteAllBytes($src, $bytes)
    $origHash = (Get-FileHash $src).Hash

    $outDir = Join-Path $dir "parts"
    $code = Run @("split", "`"$src`"", $c.Max, "`"$outDir`"")
    $parts = @(Get-ChildItem $outDir -Filter "sample data.bin.*" | Sort-Object Name)
    Check "split exit code  [$label]" ($code -eq 0)
    Check "part count = $($c.Parts)  [$label] (got $($parts.Count))" ($parts.Count -eq $c.Parts)

    $joined = Join-Path $dir "joined.bin"
    $code = Run @("join", "`"$($parts[-1].FullName)`"", "`"$joined`"")   # start from the LAST part on purpose
    Check "join exit code  [$label]" ($code -eq 0)
    Check "joined file identical  [$label]" ((Test-Path $joined) -and (Get-FileHash $joined).Hash -eq $origHash)

    # The fallback without the app should also work.
    $copyOut = Join-Path $dir "copyb.bin"
    $list = ($parts | ForEach-Object { "`"$($_.FullName)`"" }) -join " + "
    cmd /c "copy /b $list `"$copyOut`" >nul"
    Check "copy /b fallback identical  [$label]" ((Get-FileHash $copyOut).Hash -eq $origHash)
}

Check "bad size returns error" ((Run @("split", "`"$Exe`"", "abc")) -ne 0)
Check "join non-part returns error" ((Run @("join", "`"$Exe`"")) -ne 0)

Remove-Item $work -Recurse -Force
Write-Host ""
if ($failed -eq 0) { Write-Host "ALL TESTS PASSED" -ForegroundColor Green } else { Write-Host "$failed TEST(S) FAILED" -ForegroundColor Red }
exit $failed
