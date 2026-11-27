[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Koikatsu',
    [string]$CompilerPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'),
    [string]$Sb3UtilityPath,
    [string]$KkutsZip,
    [switch]$RebuildShaders
)

$ErrorActionPreference = 'Stop'
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$managed = Join-Path $GamePath 'CharaStudio_Data\Managed'
$core = Join-Path $GamePath 'BepInEx\core'
$plugins = Join-Path $GamePath 'BepInEx\plugins'
$source = Join-Path $PSScriptRoot 'BodyCum'
$resources = Join-Path $source 'Resources'
$skinBundle = Join-Path $resources 'CustomJuiceColor.bundle'
$tessBundle = Join-Path $resources 'CustomJuiceColor.KKUTS_Tess.bundle'
$accessoryBundle = Join-Path $resources 'CustomJuiceColor.Hair.bundle'
$surfaceBundle = Join-Path $resources 'CustomJuiceColor.Skin.bundle'
$bakeBundle = Join-Path $resources 'CustomJuiceColor.BustBake.bundle'
$bundles = $skinBundle, $tessBundle, $accessoryBundle, $surfaceBundle, $bakeBundle

if (!$CompilerPath) {
    $null = Get-Command dotnet -ErrorAction Stop
    $sdk = dotnet --list-sdks | Select-Object -Last 1
    if ($sdk -notmatch '^(\S+)\s+\[(.+)\]$') {
        throw 'Install a .NET SDK, or pass -CompilerPath to a Roslyn csc.exe or csc.dll.'
    }
    $CompilerPath = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')
}
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path

$missingSkinBundle = $skinBundle, $tessBundle | Where-Object { !(Test-Path -LiteralPath $_ -PathType Leaf) }
if ($RebuildShaders -or $missingSkinBundle) {
    & (Join-Path $PSScriptRoot 'Build-JuiceShader.ps1') -GamePath $GamePath `
        -Sb3UtilityPath $Sb3UtilityPath -KkutsZip $KkutsZip -OutputPath $skinBundle
}
if ($RebuildShaders -or !(Test-Path -LiteralPath $accessoryBundle -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'Build-HairJuiceShader.ps1') -GamePath $GamePath `
        -Sb3UtilityPath $Sb3UtilityPath -OutputPath $accessoryBundle
}
if ($RebuildShaders -or !(Test-Path -LiteralPath $surfaceBundle -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'Build-HairJuiceShader.ps1') -GamePath $GamePath -Sb3UtilityPath $Sb3UtilityPath -OutputPath $surfaceBundle -Skin
}

if ($RebuildShaders -or !(Test-Path -LiteralPath $bakeBundle -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'Build-HairJuiceShader.ps1') -GamePath $GamePath -Sb3UtilityPath $Sb3UtilityPath -OutputPath $bakeBundle -BustBake
}

function Find-InstalledAssembly([string]$Name, [string]$RelativePath) {
    $preferred = Join-Path $plugins $RelativePath
    if (Test-Path -LiteralPath $preferred -PathType Leaf) { return $preferred }
    $copies = @(Get-ChildItem -LiteralPath $plugins -Filter $Name -File -Recurse)
    if ($copies.Count -ne 1) { throw "Expected one installed copy of $Name in BepInEx\plugins." }
    return $copies[0].FullName
}

$references = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'UnityEngine.dll', 'UnityEngine.UI.dll',
    'Assembly-CSharp.dll', 'Assembly-CSharp-firstpass.dll' | ForEach-Object { Join-Path $managed $_ }
)
$references += (Join-Path $core 'BepInEx.dll'), (Join-Path $core '0Harmony.dll')
$references += (Find-InstalledAssembly 'KKAPI.dll' 'KKAPI.dll'),
    (Find-InstalledAssembly 'ExtensibleSaveFormat.dll' 'KK_BepisPlugins\ExtensibleSaveFormat.dll')

$output = New-Item -ItemType Directory -Path $OutputDirectory -Force
$dll = Join-Path $output.FullName 'BodyCum.dll'
$arguments = @('/noconfig', '/nologo', '/nostdlib+', '/target:library', '/optimize+', "/out:$dll")
$arguments += $references | ForEach-Object { '/reference:' + (Resolve-Path -LiteralPath $_).Path }
$arguments += ($bundles + (Join-Path $resources 'HairDirectionRegions.png')) | ForEach-Object {
    $path = (Resolve-Path -LiteralPath $_).Path
    '/resource:' + $path + ',BodyCum.Resources.' + [IO.Path]::GetFileName($path)
}
$arguments += Get-ChildItem -LiteralPath $source -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName

if ([IO.Path]::GetExtension($CompilerPath) -eq '.dll') {
    & dotnet $CompilerPath @arguments
} else {
    & $CompilerPath @arguments
}
if ($LASTEXITCODE -ne 0) { throw "BodyCum compilation failed (exit $LASTEXITCODE)." }
Get-FileHash -LiteralPath $dll -Algorithm SHA256
