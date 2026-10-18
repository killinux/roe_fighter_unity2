# Rip and import several battle stages in one go.  The AssetRipper server must be running, and
# -ServerLog must be the log file it was started with (rip.py reads it to find missing bundles):
#   E:\tools\AssetRipper_1.3.14\AssetRipper.GUI.Free.exe --headless --port 5599 --log-path _work\assetripper_stages.log
#   .\tools\rip_stages.ps1 -Stages 'l002_s01=level002_s01','l003_s03=level003_s03'
# Each entry is <short name>=<scene bundle name without scene_battlefield_ and .ab>; the stage
# then lives in Assets\ROE\stages\<short name>.
param(
    [Parameter(Mandatory = $true)][string[]]$Stages,
    [string]$ServerLog = ''
)

$project = Split-Path -Parent $PSScriptRoot
if (-not $ServerLog) { $ServerLog = Join-Path $project '_work\assetripper_stages.log' }
$ab = 'D:\Program Files (x86)\Steam\steamapps\common\Rise of Eros\RiseOfEros_Data\StreamingAssets\AssetBundles'
foreach ($entry in $Stages) {
    $short, $scene = $entry -split '=', 2
    $bundles = Join-Path $project "_work\bundles_stage_$short"
    $out = Join-Path $project "_work\ripped_stage_$short"
    New-Item -ItemType Directory -Force -Path $bundles | Out-Null
    Copy-Item -LiteralPath (Join-Path $ab "scene_battlefield_$scene.ab") -Destination $bundles -Force
    $t0 = Get-Date
    python (Join-Path $project 'tools\rip.py') --bundles $bundles --out $out --log $ServerLog | Select-Object -Last 3
    python (Join-Path $project 'tools\import_stage.py') $short | Select-Object -Last 3
    # remember which scene bundle the short name stands for (RoeFightScene reads the stage survey by it)
    python -c "import json,os,sys; p=sys.argv[1]; d=json.load(open(p)) if os.path.exists(p) else {}; d[sys.argv[2]]=sys.argv[3]; os.makedirs(os.path.dirname(p),exist_ok=True); json.dump(d,open(p,'w'),indent=1)" (Join-Path $project '_work/stage_survey/stages.json') $short $scene
    Write-Output ("{0}: {1} in {2:N0} s" -f $short, $scene, ((Get-Date) - $t0).TotalSeconds)
}
