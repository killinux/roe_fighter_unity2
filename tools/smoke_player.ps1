# Smoke test of the built player: start it straight into a CPU-vs-CPU team match, screenshot the window every few seconds,
# stop it, and keep its Player.log (any exception in there fails the test).
#   .\tools\smoke_player.ps1 [-Seconds 75] [-Out _work\smoke] [-Exe out\ROEFighter2\ROEFighter2.exe]
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'out\ROEFighter2\ROEFighter2.exe'),
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) '_work\smoke'),
    [int]$Seconds = 75,
    [string]$P1 = 'a08,g04,raven',
    [string]$P2 = 'eve_nikke06,kas,fio005',
    [int]$Team = 3
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$log = Join-Path $env:USERPROFILE 'AppData\LocalLow\ROE Fighter\ROE Fighter 2\Player.log'
Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
}
"@
[Win]::SetProcessDPIAware() | Out-Null

$args = @('-roeSelect', '0', '-roeP1', $P1, '-roeP2', $P2, '-roeTeam', "$Team", '-screen-width', '1280', '-screen-height', '720', '-screen-fullscreen', '0')
$p = Start-Process -FilePath $Exe -ArgumentList $args -PassThru
$t0 = Get-Date
# wait for the window
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 90 -and $hwnd -eq [IntPtr]::Zero; $i++) {
    Start-Sleep -Seconds 1
    $p.Refresh()
    $hwnd = $p.MainWindowHandle
}
"window after $([int]((Get-Date) - $t0).TotalSeconds) s: $hwnd"
Start-Sleep -Seconds 8
[Win]::SetForegroundWindow($hwnd) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait('{F1}')      # 1P to the computer: CPU against CPU

function Shot($name) {
    $r = New-Object Win+RECT
    [Win]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -le 0 -or $h -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}
$k = 0
while (((Get-Date) - $t0).TotalSeconds -lt $Seconds) {
    Start-Sleep -Seconds 10
    $k++
    Shot ("shot{0:D2}_{1}s" -f $k, [int]((Get-Date) - $t0).TotalSeconds)
    if ($p.HasExited) { "the player exited early (code $($p.ExitCode))"; break }
}
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
Start-Sleep -Seconds 2
Copy-Item -LiteralPath $log -Destination (Join-Path $Out 'Player.log') -Force
$bad = Select-String -LiteralPath (Join-Path $Out 'Player.log') -Pattern 'Exception|NullReference|IndexOutOfRange|MissingReference' | Select-Object -First 10
"screenshots: $k, log lines: $((Get-Content (Join-Path $Out 'Player.log')).Count), exceptions: $(@($bad).Count)"
$bad | ForEach-Object { $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length)) }
Select-String -LiteralPath (Join-Path $Out 'Player.log') -Pattern '\[ROE\]' | Select-Object -First 12 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(160, $_.Line.Length)) }
