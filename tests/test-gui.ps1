# Drives the FileSplitter window through UI Automation: split, join, cancel.
# Must run in an interactive desktop session.
# Usage: powershell -ExecutionPolicy Bypass -File test-gui.ps1 [-Exe path] [-Work dir]
param(
    [string]$Exe = (Join-Path $PSScriptRoot "FileSplitter.exe"),
    [string]$Work = (Join-Path $env:TEMP "fs_gui_test")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

Add-Type -Namespace "" -Name Win32 -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam); [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title); [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title); [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd); [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);'
$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$log = Join-Path $Work "gui-result.txt"
New-Item -ItemType Directory -Force $Work | Out-Null
Set-Content $log ""
$failed = 0
$shot = 0

function Log($m) { Add-Content $log $m; Write-Host $m }
function Check($name, [bool]$ok) {
    if ($ok) { Log "PASS  $name" } else { Log "FAIL  $name"; $script:failed++ }
}

function Screenshot($label) {
    $script:shot++
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $bmp.Save((Join-Path $Work ("shot{0}-{1}.png" -f $script:shot, $label)), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

function WaitUntil([scriptblock]$probe, [int]$seconds = 30, [string]$what = "condition") {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        $r = & $probe
        if ($r) { return $r }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for $what"
}

function ByName($root, $name, $type = $null) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    if ($type) {
        $t = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type)
        $c = New-Object System.Windows.Automation.AndCondition($c, $t)
    }
    WaitUntil { $root.FindFirst($Scope::Descendants, $c) } 15 "element '$name'"
}

function TextLike($root, $pattern) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($e in $root.FindAll($Scope::Descendants, $c)) { if ($e.Current.Name -match $pattern) { return $e } }
    return $null
}

function SetText($root, $name, $value) {
    $e = ByName $root $name
    $vp = $null
    if (-not $e.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$vp)) {
        $edit = $e.FindFirst($Scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
        $vp = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    }
    $vp.SetValue($value)
}

function Click($root, $name) {
    (ByName $root $name ([System.Windows.Automation.ControlType]::Button)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function SelectTab($root, $name) {
    (ByName $root $name ([System.Windows.Automation.ControlType]::TabItem)).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

# Waits for the message box, checks its text, screenshots it, presses OK.
# MessageBox windows use the standard dialog class "#32770".
function FindDialog($win) {
    $cls = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, "#32770")
    $d = $win.FindFirst($Scope::Descendants, $cls)
    if ($d) { return $d }
    $pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    return $AE::RootElement.FindFirst($Scope::Children, (New-Object System.Windows.Automation.AndCondition($cls, $pidCond)))
}

function DialogElement($dialog, $pattern) {
    foreach ($e in $dialog.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($e.Current.Name -match $pattern) { return $e }
    }
    return $null
}

# Win32 message boxes expose their text and OK button as plain panes.
function DismissMessage($win, $pattern, $label) {
    $text = WaitUntil {
        $d = FindDialog $win
        if ($d) { DialogElement $d $pattern }
    } 120 "message box matching '$pattern'"
    $message = $text.Current.Name
    Log "      message: $message"
    Start-Sleep -Milliseconds 400
    Screenshot $label
    # Press OK the way Windows does: BM_CLICK on the button, WM_CLOSE as a fallback.
    $hwnd = [Win32]::FindWindow("#32770", "File Splitter")
    $okBtn = [Win32]::FindWindowEx($hwnd, [IntPtr]::Zero, "Button", "OK")
    [void][Win32]::SetForegroundWindow($hwnd)
    $ignored = [IntPtr]::Zero
    [void][Win32]::SendMessageTimeout($okBtn, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero, 2, 3000, [ref]$ignored)
    Start-Sleep -Milliseconds 700
    if ([Win32]::IsWindow($hwnd)) {
        Log "      BM_CLICK did not close the message box, sending WM_CLOSE"
        [void][Win32]::PostMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    WaitUntil { -not [Win32]::IsWindow($hwnd) } 10 "message box to close" | Out-Null
    return $message
}

$proc = $null
try {
    $sample = Join-Path $Work "sample video.mp4"
    $partsDir = Join-Path $Work "parts"
    $joined = Join-Path $Work "joined.mp4"
    Remove-Item $partsDir, $joined, (Join-Path $Work "parts-cancel") -Recurse -Force -ErrorAction SilentlyContinue
    $bytes = New-Object byte[] 45000000
    (New-Object Random 7).NextBytes($bytes)
    [IO.File]::WriteAllBytes($sample, $bytes)
    $origHash = (Get-FileHash $sample).Hash

    $proc = Start-Process $Exe -PassThru
    $mainCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
    $win = WaitUntil { $AE::RootElement.FindFirst($Scope::Children, $mainCond) } 60 "main window"
    Check "window opens with title 'File Splitter' (got '$($win.Current.Name)')" ($win.Current.Name -eq "File Splitter")

    # ---- Split: 45 MB file, 10240 KB (= 10 MB) max -> 5 parts ----
    SetText $win "File to split" $sample
    SetText $win "Output folder" $partsDir
    SetText $win "Max part size" "10240"
    $unit = ByName $win "Size unit"
    $unit.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 300
    (ByName $unit "KB").GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $unit.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()

    $info = WaitUntil { TextLike $win "part\(s\)" } 10 "split info label"
    Log "      split info: $($info.Current.Name)"
    Check "split info predicts 5 parts" ($info.Current.Name -match "42\.92 MB.*5 part\(s\)")
    Screenshot "split-ready"

    Click $win "Split"
    $msg = DismissMessage $win "^Created" "split-done"
    Check "split success message" ($msg -match "Created 5 part")
    $parts = @(Get-ChildItem $partsDir | Sort-Object Name)
    Check "5 part files created (got $($parts.Count))" ($parts.Count -eq 5)
    Check "no part larger than 10 MB" (-not ($parts | Where-Object Length -gt 10485760))
    Check "parts named .001-.005" (($parts.Name -join ",") -eq "sample video.mp4.001,sample video.mp4.002,sample video.mp4.003,sample video.mp4.004,sample video.mp4.005")

    # ---- Join: pick the middle part, custom output ----
    SelectTab $win "Join"
    Start-Sleep -Milliseconds 300
    SetText $win "Part file" $parts[2].FullName
    $joinInfo = WaitUntil { TextLike $win "^Found" } 10 "join info label"
    Log "      join info: $($joinInfo.Current.Name)"
    Check "join info finds 5 parts" ($joinInfo.Current.Name -match "Found 5 part\(s\)")
    $defaultOut = (ByName $win "Output file").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Check "default join output is original name (got '$defaultOut')" ($defaultOut -eq (Join-Path $partsDir "sample video.mp4"))
    SetText $win "Output file" $joined
    Screenshot "join-ready"

    Click $win "Join"
    $msg = DismissMessage $win "^Joined" "join-done"
    Check "join success message" ($msg -match "Joined 5 part")
    Check "joined file identical to original" ((Test-Path $joined) -and (Get-FileHash $joined).Hash -eq $origHash)

    # ---- Cancel: 1 KB parts => ~44k files, cancel mid-way, nothing left behind ----
    SelectTab $win "Split"
    Start-Sleep -Milliseconds 300
    $cancelDir = Join-Path $Work "parts-cancel"
    SetText $win "Output folder" $cancelDir
    SetText $win "Max part size" "1"
    Start-Sleep -Milliseconds 200
    # Force the NumericUpDown to commit by toggling the unit away and back.
    $unit.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 300
    (ByName $unit "MB").GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $unit.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 300
    (ByName $unit "KB").GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $unit.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()

    $sw = [Diagnostics.Stopwatch]::StartNew()
    Click $win "Split"
    WaitUntil { (Test-Path $cancelDir) -and @(Get-ChildItem $cancelDir).Count -gt 200 } 30 "split to be in progress" | Out-Null
    Screenshot "cancel-in-progress"
    Click $win "Cancel"
    WaitUntil { TextLike $win "^Cancelled" } 60 "cancelled status" | Out-Null
    Log "      cancelled after $([int]$sw.Elapsed.TotalSeconds)s"
    Start-Sleep -Milliseconds 500
    Screenshot "cancelled"
    $left = if (Test-Path $cancelDir) { @(Get-ChildItem $cancelDir).Count } else { 0 }
    Check "cancel removes partial parts (left: $left)" ($left -eq 0)

    $win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    WaitUntil { $proc.HasExited } 10 "app to exit" | Out-Null
    Check "app closes cleanly" $proc.HasExited
}
catch {
    Log "FAIL  exception: $($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
    try {
        $pidCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
        foreach ($w in $AE::RootElement.FindAll($Scope::Children, $pidCond)) {
            Log ("      top-level: '{0}' class={1} type={2} hwnd={3}" -f $w.Current.Name, $w.Current.ClassName, $w.Current.ControlType.ProgrammaticName, $w.Current.NativeWindowHandle)
            foreach ($e in $w.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                Log ("        '{0}' class={1} type={2}" -f $e.Current.Name, $e.Current.ClassName, $e.Current.ControlType.ProgrammaticName)
            }
        }
    } catch { Log "      dump failed: $($_.Exception.Message)" }
    $failed++
    try { Screenshot "error" } catch {}
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
    Remove-Item (Join-Path $Work "parts-cancel") -Recurse -Force -ErrorAction SilentlyContinue
    Log ""
    if ($failed -eq 0) { Log "ALL GUI TESTS PASSED" } else { Log "$failed GUI TEST(S) FAILED" }
}
exit $failed
