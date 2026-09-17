<#
.SYNOPSIS
    驗證拖曳與右鍵選單共用的圖示轉移規則；只使用記憶體資料，不修改使用者設定。
#>
param(
    [string]$BinaryDir = (Join-Path $PSScriptRoot '..\Code\Desktop Frames\bin\MoveVerify')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$binaryDirPath = [System.IO.Path]::GetFullPath($BinaryDir)
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $binaryDirPath 'Newtonsoft.Json.dll'))
$appAssembly = [System.Reflection.Assembly]::LoadFrom((Join-Path $binaryDirPath 'Desktop Frames.dll'))
$transferType = $appAssembly.GetType('Desktop_Frames.FrameItemTransfer', $true)
$moveMethod = $transferType.GetMethod('Move', [System.Reflection.BindingFlags]'Static, NonPublic')

function Get-PathCount($items, [string]$filename) {
    return @($items | Where-Object { $_['Filename'].ToString() -eq $filename }).Count
}

function Assert-Location($source, $target, [string]$filename, [int]$sourceCount, [int]$targetCount) {
    if ((Get-PathCount $source $filename) -ne $sourceCount -or
        (Get-PathCount $target $filename) -ne $targetCount) {
        throw "圖示位置錯誤：預期來源 $sourceCount、目標 $targetCount：$filename"
    }
}

function Move-Item($source, $target, $selected, [string]$filename) {
    return $moveMethod.Invoke($null, @($source, $target, $selected, $filename, $target.Count))
}

# 正常來回：兩次移動後來源與目標各自只會有 0 / 1 筆。
$ai = [Newtonsoft.Json.Linq.JArray]::Parse('[{"Filename":"Shortcuts\\GitHub.lnk","DisplayOrder":0}]')
$shortcuts = [Newtonsoft.Json.Linq.JArray]::Parse('[]')
Move-Item $ai $shortcuts $ai[0] 'Shortcuts\GitHub.lnk' | Out-Null
Assert-Location $ai $shortcuts 'Shortcuts\GitHub.lnk' 0 1
Move-Item $shortcuts $ai $shortcuts[0] 'Shortcuts\GitHub.lnk' | Out-Null
Assert-Location $shortcuts $ai 'Shortcuts\GitHub.lnk' 0 1

# 模擬重新載入 JSON 後再移動：不依賴舊的 JToken 實例。
$ai = [Newtonsoft.Json.Linq.JArray]::Parse($ai.ToString())
$shortcuts = [Newtonsoft.Json.Linq.JArray]::Parse($shortcuts.ToString())
Move-Item $ai $shortcuts $ai[0] 'Shortcuts\GitHub.lnk' | Out-Null
Assert-Location $ai $shortcuts 'Shortcuts\GitHub.lnk' 0 1

# 歷史殘留：來源 2 筆、目標已有 1 筆時，移動後來源歸零且目標仍只有 1 筆。
$ai = [Newtonsoft.Json.Linq.JArray]::Parse('[{"Filename":"Shortcuts\\GitHub.lnk"},{"Filename":"Shortcuts\\GitHub.lnk"}]')
$shortcuts = [Newtonsoft.Json.Linq.JArray]::Parse('[{"Filename":"Shortcuts\\GitHub.lnk"}]')
$result = Move-Item $ai $shortcuts $ai[0] 'Shortcuts\GitHub.lnk'
Assert-Location $ai $shortcuts 'Shortcuts\GitHub.lnk' 0 1
if ($result.Item1 -ne 2 -or $result.Item3) { throw '歷史殘留修復結果不符預期。' }

# 目標端歷史重複也只保留第一筆；同時保留其他路徑。
$ai = [Newtonsoft.Json.Linq.JArray]::Parse('[{"Filename":"Shortcuts\\GitHub.lnk"},{"Filename":"Other.lnk"}]')
$shortcuts = [Newtonsoft.Json.Linq.JArray]::Parse('[{"Filename":"Shortcuts\\GitHub.lnk"},{"Filename":"Shortcuts\\GitHub.lnk"}]')
$result = Move-Item $ai $shortcuts $ai[0] 'Shortcuts\GitHub.lnk'
Assert-Location $ai $shortcuts 'Shortcuts\GitHub.lnk' 0 1
if ($result.Item2 -ne 1 -or (Get-PathCount $ai 'Other.lnk') -ne 1) { throw '目標去重或其他圖示保留失敗。' }

# 防止同清單移動或舊物件誤刪目前資料。
$sameListRejected = $false
try { Move-Item $ai $ai $ai[0] 'Other.lnk' | Out-Null } catch { $sameListRejected = $true }
if (-not $sameListRejected -or (Get-PathCount $ai 'Other.lnk') -ne 1) { throw '同清單防護失敗。' }

$staleSelected = [Newtonsoft.Json.Linq.JArray]::Parse('[{"Filename":"Other.lnk"}]')[0]
$staleRejected = $false
try { Move-Item $ai $shortcuts $staleSelected 'Other.lnk' | Out-Null } catch { $staleRejected = $true }
if (-not $staleRejected -or (Get-PathCount $ai 'Other.lnk') -ne 1) { throw '舊物件防護失敗。' }

Write-Output '圖示轉移回歸測試通過：來回、重新載入、來源殘留、目標重複、其他圖示保留、同清單及舊物件防護。'
