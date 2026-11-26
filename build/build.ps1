param(
    [Parameter(Mandatory=$true)][string]$GameDir
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src  = Join-Path $root 'src\Plugin.cs'
$out  = Join-Path $root 'bin\PE_XpHelper.dll'
$csc  = Join-Path $env:ProgramFiles 'dotnet\sdk\6.0.410\Roslyn\bincore\csc.dll'
if (-not (Test-Path -LiteralPath $csc)) { throw '找不到 Roslyn csc.dll，请修改 build.ps1 中的路径' }

$m = Join-Path $GameDir 'Project Exhibitionist_Data\Managed'
$refs = @(
    (Join-Path $m 'mscorlib.dll'), (Join-Path $m 'netstandard.dll'), (Join-Path $m 'System.Core.dll'),
    (Join-Path $GameDir 'BepInEx\core\BepInEx.dll'), (Join-Path $GameDir 'BepInEx\core\0Harmony.dll'),
    (Join-Path $m 'UnityEngine.dll'), (Join-Path $m 'UnityEngine.CoreModule.dll'),
    (Join-Path $m 'UnityEngine.IMGUIModule.dll'), (Join-Path $m 'UnityEngine.InputLegacyModule.dll'),
    (Join-Path $m 'UnityEngine.TextRenderingModule.dll'), (Join-Path $m 'Assembly-CSharp.dll')
)
$args = @('/noconfig','/nostdlib+','/target:library','/platform:anycpu','/optimize+',"/out:$out",'/langversion:9','/codepage:65001')
foreach ($r in $refs) { $args += "/reference:$r" }
$args += $src
& dotnet $csc $args
if ($LASTEXITCODE -ne 0) { throw "编译失败: $LASTEXITCODE" }
Write-Host "已生成: $out"