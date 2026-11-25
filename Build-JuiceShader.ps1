[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Koikatsu',
    [string]$Sb3UtilityPath,
    [string]$OutputPath = (Join-Path $PSScriptRoot 'BodyCum\Resources\CustomJuiceColor.bundle'),
    [string]$KkutsZip,
    [switch]$StockOnly
)

$ErrorActionPreference = 'Stop'
if (!$Sb3UtilityPath) {
    $Sb3UtilityPath = Join-Path $GamePath '[MODDING] Tools\SB3Utility v23.1.7'
}
$Sb3UtilityPath = (Resolve-Path -LiteralPath $Sb3UtilityPath).Path
$gameBundle = (Resolve-Path -LiteralPath (Join-Path $GamePath 'abdata\chara\mm_base.unity3d')).Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$compiler = (Resolve-Path -LiteralPath $framework).Path
$tools = Join-Path $PSScriptRoot 'ShaderBuild'
$build = Join-Path $PSScriptRoot 'obj\JuiceShader'
$outputFolder = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Path $build, $outputFolder -Force | Out-Null

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE)." }
}

$pack = Join-Path $build 'PackSkinShader.exe'
$patch = Join-Path $build 'PatchJuiceShader.exe'
$original = Join-Path $build 'main_skin.d3d11'
$patched = Join-Path $build 'main_skin.custom.d3d11'
$patchTools = @((Join-Path $tools 'Dxbc.cs'), (Join-Path $tools 'AbdomenJuiceMask.cs'))
$refs = 'SB3Utility.dll', 'LZ4.dll', 'plugins\UnityBase.dll', 'plugins\SB3UtilityPP.dll' |
    ForEach-Object { '/reference:' + (Resolve-Path -LiteralPath (Join-Path $Sb3UtilityPath $_)).Path }

Invoke-Checked $compiler (@('/nologo', '/platform:x64', '/optimize+', "/out:$pack", (Join-Path $tools 'PackSkinShader.cs')) + $refs)
Invoke-Checked $compiler (@('/nologo', '/platform:x64', '/optimize+', "/out:$patch",
    (Join-Path $tools 'PatchJuiceShader.cs')) + $patchTools)
Invoke-Checked $pack @($Sb3UtilityPath, $gameBundle, $original, '--extract')
Invoke-Checked $patch @($original, $patched)
Invoke-Checked $pack @($Sb3UtilityPath, $gameBundle, $OutputPath, $patched)
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256

if ($StockOnly) { return }
if (!$KkutsZip) {
    $modsPath = Join-Path $GamePath 'mods'
    $installedArchives = @(Get-ChildItem -LiteralPath $modsPath -Filter '*KKUTS*v2.3*.zipmod' -File -Recurse)
    if ($installedArchives.Count -ne 1) {
        throw 'Pass the installed nHaruka KKUTS v2.3 archive with -KkutsZip, or use -StockOnly.'
    }
    $KkutsZip = $installedArchives[0].FullName
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$patchUts = Join-Path $build 'PatchKkutsJuiceShader.exe'
Invoke-Checked $compiler (@('/nologo', '/platform:x64', '/optimize+', "/out:$patchUts",
    (Join-Path $tools 'PatchKkutsJuiceShader.cs')) + $patchTools)

$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $KkutsZip).Path)
try {
    $entryName = 'abdata/chara/kkutstess.unity3d'
    $entry = $zip.GetEntry($entryName)
    if (!$entry) { throw "Shader bundle $entryName is missing from the KKUTS archive." }

    $inputBundle = Join-Path $build 'kkutstess.unity3d'
    $inputBlob = Join-Path $build 'KKUTS_Tess.d3d11'
    $patchedBlob = Join-Path $build 'KKUTS_Tess.custom.d3d11'
    $bundle = Join-Path $outputFolder 'CustomJuiceColor.KKUTS_Tess.bundle'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $inputBundle, $true)
    Invoke-Checked $pack @($Sb3UtilityPath, $inputBundle, $inputBlob, '--extract', 'KKUTS_Tess')
    Invoke-Checked $patchUts @($inputBlob, $patchedBlob)
    Invoke-Checked $pack @($Sb3UtilityPath, $inputBundle, $bundle, $patchedBlob, 'KKUTS_Tess')
    Get-FileHash -LiteralPath $bundle -Algorithm SHA256
} finally {
    $zip.Dispose()
}
