# Run one editor method in batch mode and show the lines of the log that matter.
#   .\tools\unity_batch.ps1 -Method RoeFighter.EditorTools.RoeProjectSetup.Run
#   .\tools\unity_batch.ps1 -Method X.Y.Z -Name capture -Extra '-roeOut','E:\x' -Graphics
# -Graphics keeps the GPU (needed to render); without it the editor starts with -nographics.
# -NoQuit: the method keeps the editor running and quits it itself (play mode: RoeClothPlayDemo).
param(
    [Parameter(Mandatory = $true)][string]$Method,
    [string]$Name = '',
    [string[]]$Extra = @(),
    [switch]$Graphics,
    [switch]$NoQuit,
    [int]$TimeoutMinutes = 60
)

$unity = 'E:\tools\Unity\Hub\Editor\6000.4.12f1\Editor\Unity.exe'
$project = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $project '_work\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
if (-not $Name) { $Name = ($Method -split '\.')[-2] + '_' + ($Method -split '\.')[-1] }
$log = Join-Path $logDir ("{0}_{1}.log" -f (Get-Date -Format 'MMdd_HHmmss'), $Name)

$lock = Join-Path $project 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lock) {
    # the Hub runs a helper that is also called "unity"; only the editor binary counts
    $running = Get-Process -Name Unity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $unity }
    if ($running) { Write-Output "Unity is already running (pid $($running.Id -join ', ')) - the project may be open; aborting."; exit 3 }
}

# No proxy for the editor (10-17): with HTTP_PROXY / HTTPS_PROXY set, the IL post processor's trigger sends its call to the
# local runner through the proxy and crashes (Unity.ILPP.Trigger.exe 0xc0000409 every ~16 s) - the compile then waits
# forever ("Connectivity with IL Post Processor runner cannot be established yet. Retrying.", CPU 0)
Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY -ErrorAction SilentlyContinue
$env:NO_PROXY = 'localhost,127.0.0.1,::1'

$unityArgs = @('-batchmode', '-projectPath', $project, '-executeMethod', $Method)
if (-not $NoQuit) { $unityArgs += '-quit' }
$unityArgs += @('-logFile', $log)
if (-not $Graphics) { $unityArgs += '-nographics' }
$unityArgs += $Extra

$t0 = Get-Date
$p = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru
# The same endless "Retrying." when the machine is busy (10-24: another window's seven Blender jobs at 100% CPU): the editor
# starts the IL post processor's runner below normal priority, it never gets the CPU to open its pipe, and the trigger that
# waits for the pipe gives up and crashes, again and again.  So while the editor runs, its post processor runs at normal
# priority, like everything else.
$deadline = $t0.AddMinutes($TimeoutMinutes)
while (-not $p.WaitForExit(1000)) {
    if ((Get-Date) -gt $deadline) { $p.Kill(); Write-Output "TIMEOUT after $TimeoutMinutes min - killed"; break }
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'Unity.ILPP.*' } |
        ForEach-Object {
            $c = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
            if ($c -and $c.PriorityClass -ne 'Normal') { try { $c.PriorityClass = 'Normal' } catch { } }
        }
}
$secs = [int]((Get-Date) - $t0).TotalSeconds
Write-Output "exit code $($p.ExitCode) after $secs s, log $log"

$pattern = '\[ROE\]|error CS\d+|warning CS\d+.*RoeFighter|Shader error|Shader warning in ''ROE|Exception:|Aborting batchmode|executeMethod class|Scripts have compiler errors|No valid Unity Editor license|Assertion failed|Crash!!!'
Select-String -LiteralPath $log -Pattern $pattern | ForEach-Object { $_.Line } | Select-Object -Unique -First 80 |
    ForEach-Object { if ($_.Length -gt 300) { $_.Substring(0, 300) } else { $_ } }
exit $p.ExitCode
