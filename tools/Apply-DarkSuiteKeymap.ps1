<#
.SYNOPSIS
    让 Dark Suite 2.6.3 让出 F7-F11，避免与 PE_Trainer / PE Xp Helper 的热键冲突。

.DESCRIPTION
    Dark Suite 2.6.3 的默认热键里，F7 / F8 / F9 / F10 / F11 被 MinimapPlus、InfiniteStamina、
    SlowDetection 三个模块占用；PE_Trainer v1.4 硬编码了 F5-F11，PE Xp Helper v1.3.0 起面板键
    默认是 Insert。本脚本把 Dark Suite 这几个冲突项统一加上 LeftControl 前缀，做到三方向零重叠。

    改写目标（BepInEx\config\com.darksuite.pe.cfg）：
        [MinimapPlus.Hotkeys]    ToggleNpc       F7          -> LeftControl + F7
        [InfiniteStamina.Hotkeys] Toggle         F8          -> LeftControl + F8
        [SlowDetection.Hotkeys]  ToggleTwoPhase  F9          -> LeftControl + F9
        [SlowDetection.Hotkeys]  Slower          F10         -> LeftControl + F10
        [SlowDetection.Hotkeys]  Faster          F11         -> LeftControl + F11

    不含冲突的项（F1-F4、F12、End、Pause、Delete、小键盘）保持原样不动。

.PARAMETER GameDir
    Project Exhibitionist 游戏根目录（含 Project Exhibitionist.exe）。

.PARAMETER ConfigPath
    直接指定 cfg 路径，优先于 -GameDir。

.PARAMETER DryRun
    只打印将要发生的变更，不写文件。

.EXAMPLE
    .\Apply-DarkSuiteKeymap.ps1 -GameDir "D:\sbeam\steamapps\common\ProjectExhibitionist" -DryRun

.EXAMPLE
    .\Apply-DarkSuiteKeymap.ps1 -GameDir "D:\sbeam\steamapps\common\ProjectExhibitionist"
#>
[CmdletBinding(DefaultParameterSetName = 'ByGameDir')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'ByGameDir')]
    [string]$GameDir,

    [Parameter(Mandatory = $true, ParameterSetName = 'ByConfig')]
    [string]$ConfigPath,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# ---- 目标键位表：Section -> Key -> 新值 ----
$Keymap = [ordered]@{
    'MinimapPlus.Hotkeys'     = [ordered]@{ ToggleNpc      = 'LeftControl + F7' }
    'InfiniteStamina.Hotkeys' = [ordered]@{ Toggle         = 'LeftControl + F8' }
    'SlowDetection.Hotkeys'   = [ordered]@{
        ToggleTwoPhase = 'LeftControl + F9'
        Slower         = 'LeftControl + F10'
        Faster         = 'LeftControl + F11'
    }
}

# ---- 解析配置路径 ----
if ($PSCmdlet.ParameterSetName -eq 'ByGameDir') {
    if (-not (Test-Path -LiteralPath $GameDir)) { throw "游戏目录不存在: $GameDir" }
    $exe = Join-Path $GameDir 'Project Exhibitionist.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "这不是游戏根目录（缺少 Project Exhibitionist.exe）: $GameDir" }
    $ConfigPath = Join-Path $GameDir 'BepInEx\config\com.darksuite.pe.cfg'
}

Write-Host "目标配置: $ConfigPath" -ForegroundColor Cyan

$exists = Test-Path -LiteralPath $ConfigPath

if (-not $exists) {
    # 首次使用：生成一份只含冲突项的最小配置，BepInEx 启动时会自动补全其余默认项
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('## Dark Suite 键位避让配置（由 pe-xphelper/tools/Apply-DarkSuiteKeymap.ps1 生成）')
    [void]$sb.AppendLine('## BepInEx 首次加载时会自动补齐缺失的默认项，这里只固定与其它插件冲突的热键。')
    foreach ($section in $Keymap.Keys) {
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine("[$section]")
        foreach ($key in $Keymap[$section].Keys) {
            [void]$sb.AppendLine("$key = $($Keymap[$section][$key])")
        }
    }
    if ($DryRun) {
        Write-Host "`n[DRY-RUN] 配置文件不存在，将新建并写入以下内容：" -ForegroundColor Yellow
        Write-Host $sb.ToString()
        return
    }
    $dir = Split-Path -Parent $ConfigPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($ConfigPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "已新建配置文件（BepInEx 启动后会补全其余默认项）" -ForegroundColor Green
    return
}

# ---- 已存在：备份 + 逐行改写 ----
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "$ConfigPath.bak-$stamp"

$lines   = [System.IO.File]::ReadAllLines($ConfigPath)
$current = ''
$changes = New-Object System.Collections.Generic.List[string]
$out     = New-Object System.Collections.Generic.List[string]

foreach ($line in $lines) {
    $trim = $line.Trim()

    if ($trim -match '^\[(.+)\]$') {
        $current = $Matches[1].Trim()
        $out.Add($line)
        continue
    }

    if ($Keymap.Contains($current) -and $trim -match '^([\w\.\-]+)\s*=\s*(.*)$') {
        $key = $Matches[1]
        $val = $Matches[2].Trim()
        if ($Keymap[$current].Contains($key)) {
            $new  = $Keymap[$current][$key]
            $norm = ($val -replace '\s+', ' ')
            if ($norm -ne $new) {
                $changes.Add("[$current] $key : '$val' -> '$new'")
                $indent = [regex]::Match($line, '^\s*').Value
                $out.Add("$indent$key = $new")
                continue
            } else {
                $changes.Add("[$current] $key : 已是 '$new'（跳过）")
            }
        }
    }
    $out.Add($line)
}

if ($changes.Count -eq 0) {
    Write-Host "没有找到任何目标键位，配置可能不是 Dark Suite 2.6.3 生成的。未做修改。" -ForegroundColor Yellow
    return
}

Write-Host "`n变更清单：" -ForegroundColor Cyan
$changes | ForEach-Object { Write-Host "  $_" }

if ($DryRun) {
    Write-Host "`n[DRY-RUN] 未写入任何文件。" -ForegroundColor Yellow
    return
}

Copy-Item -LiteralPath $ConfigPath -Destination $backup -Force
Write-Host "`n已备份: $backup" -ForegroundColor DarkGray

[System.IO.File]::WriteAllLines($ConfigPath, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "已写入: $ConfigPath" -ForegroundColor Green
Write-Host "下次启动游戏生效（BepInEx 在插件加载时读取该配置）。" -ForegroundColor Green
