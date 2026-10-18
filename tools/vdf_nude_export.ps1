# Vindictus' new-rig female base body SM_Fiona_Body01 (Character/Player/BaseBody_PCF/Model: a STATIC mesh - the archive's
# lister in ripper_tpose scripts/vindictus only takes SK_* parts, so it was never exported) with its skin materials, as
# PSKX + PNG by UE Viewer, for tools/vdf_nude.py (Fiona's nude base for the clothes burst).
#   .\tools\vdf_nude_export.ps1 [-Out E:\tools\vindictus\_scratch_export\fiona_body01]
# The AES key is read from the game tools' own file and handed to UE Viewer through a temp file that is deleted
# afterwards; it is never printed or written anywhere else. Game data: personal use only, nothing of it goes into git.
param(
    [string]$Out = 'E:\tools\vindictus\_scratch_export\fiona_body01',
    [string]$Umodel = 'E:\tools\umodel_specific\materials\umodel_materials_ue5.exe',
    [string]$Paks = 'E:\tools\vindictus\Vindictus\Content\Paks',
    [string]$KeyFile = 'E:\tools\vindictus\_download\aes_key.txt'
)

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$aesKey = (Get-Content -LiteralPath $KeyFile -Raw).Trim()
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("vindictus-aes-{0}.txt" -f [guid]::NewGuid())
[System.IO.File]::WriteAllText($tmp, $aesKey)
Remove-Variable aesKey
try {
    foreach ($pkg in @('VindictusRoot/Character/Player/BaseBody_PCF/Model/SM_Fiona_Body01',
                       'VindictusRoot/Character/Player/BaseBody_PCF/Model/MI_PCF_Upper01',
                       'VindictusRoot/Character/Player/BaseBody_PCF/Model/MI_PCF_Lower01',
                       'VindictusRoot/Character/Player/BaseBody_PCF/Model/MI_PCF_Hand01',
                       'VindictusRoot/Character/Player/BaseBody_PCF/Model/MI_PCF_Body01',
                       'VindictusRoot/Character/Player/BaseBody_PCF/Model/MI_PCF_HandFoot01')) {
        # PowerShell would split "-game=ue5.3" at the dot when passed bare: quoted
        $o = & $Umodel '-game=ue5.3' "-path=$Paks" "-aes=@$tmp" '-export' '-png' "-out=$Out" $pkg 2>&1
        $summary = ($o | Select-String -Pattern '^Exported \d+/\d+' | Select-Object -Last 1)
        Write-Output ("{0}: {1}" -f $pkg.Split('/')[-1], $(if ($summary) { $summary.Line } else { ($o | Select-Object -Last 3) -join ' | ' }))
    }
}
finally {
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
}
Get-ChildItem -Recurse $Out -File | Select-Object @{n = 'file'; e = { $_.FullName.Substring($Out.Length) } }, Length | Format-Table -AutoSize | Out-String -Width 200
