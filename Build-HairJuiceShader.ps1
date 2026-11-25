[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Koikatsu',
    [string]$Sb3UtilityPath,
    [string]$OutputPath,
    [switch]$Skin
)

$ErrorActionPreference = 'Stop'
$variant = if ($Skin) { 'Skin' } else { 'Hair' }
if (!$OutputPath) {
    $OutputPath = Join-Path $PSScriptRoot ('BodyCum\Resources\CustomJuiceColor.' + $variant + '.bundle')
}
if (!$Sb3UtilityPath) {
    $Sb3UtilityPath = Join-Path $GamePath '[MODDING] Tools\SB3Utility v23.1.7'
}
$Sb3UtilityPath = (Resolve-Path -LiteralPath $Sb3UtilityPath).Path
$gameBundle = (Resolve-Path -LiteralPath (Join-Path $GamePath 'abdata\chara\mm_base.unity3d')).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$tools = Join-Path $PSScriptRoot 'ShaderBuild'
$build = Join-Path $PSScriptRoot 'obj\HairJuiceShader'
New-Item -ItemType Directory -Path $build, (Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))) -Force | Out-Null
$pack = Join-Path $build 'PackHairShader.exe'
$refs = 'SB3Utility.dll', 'LZ4.dll', 'plugins\UnityBase.dll', 'plugins\SB3UtilityPP.dll' |
    ForEach-Object { '/reference:' + (Resolve-Path -LiteralPath (Join-Path $Sb3UtilityPath $_)).Path }
$sources = 'Dxbc.cs', 'PackSkinShader.cs', 'PackHairShader.cs' | ForEach-Object { Join-Path $tools $_ }
& $compiler /nologo /platform:x64 /optimize+ /main:PackHairShader "/out:$pack" @refs @sources
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the hair shader packing tool.' }
$options = @()
if ($Skin) { $options += '--skin' }
& $pack $Sb3UtilityPath $gameBundle (Join-Path $tools ($variant + 'JuiceProjection.hlsl')) $OutputPath @options
if ($LASTEXITCODE -ne 0) { throw 'Could not build the hair shader AssetBundle.' }
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
