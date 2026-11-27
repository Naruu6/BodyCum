[CmdletBinding()]
param(
    [string]$RoslynPath='C:/Koikatsu/[MODDING] Tools/dnSpy v6.1.5/bin',
    [string]$FrameworkCompilerPath='C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
)
$ErrorActionPreference='Stop'
$compiler=(Resolve-Path -LiteralPath $FrameworkCompilerPath).Path
$framework=Split-Path $compiler
$roslyn=(Resolve-Path -LiteralPath $RoslynPath).Path
$references=@('Microsoft.CodeAnalysis.dll','Microsoft.CodeAnalysis.CSharp.dll','System.Collections.Immutable.dll','System.Reflection.Metadata.dll') | ForEach-Object { Join-Path $roslyn $_ }
foreach($reference in $references){if(!(Test-Path -LiteralPath $reference -PathType Leaf)){throw "Missing compiler dependency: $reference"}}
$references+=@('System.dll','System.Core.dll','netstandard.dll','System.Runtime.dll','System.Collections.dll','System.Reflection.dll','System.IO.dll','System.Linq.dll','System.Threading.dll','System.Text.Encoding.dll','System.Runtime.Extensions.dll','System.Threading.Tasks.dll') | ForEach-Object {Join-Path $framework $_} | Where-Object {Test-Path -LiteralPath $_}
$escaped=$roslyn.Replace('"','""')
$pathSource=Join-Path $PSScriptRoot 'InstalledCompilerPath.cs'
('internal static class InstalledCompilerPath { internal const string RoslynDirectory = @"' + $escaped + '"; }') | Set-Content -LiteralPath $pathSource -Encoding UTF8
$arguments=@('/noconfig','/nologo','/target:exe','/optimize+',('/out:'+ (Join-Path $PSScriptRoot 'RoslynCscHost.exe')))
$arguments+=$references | ForEach-Object {'/reference:'+$_}
$arguments+=(Join-Path $PSScriptRoot 'RoslynCscHost.cs'),$pathSource
& $compiler @arguments
if($LASTEXITCODE-ne0){throw 'Local Roslyn host bootstrap failed'}
& (Join-Path $PSScriptRoot 'RoslynCscHost.exe') /version
if($LASTEXITCODE-ne0){throw 'Local Roslyn host load failed'}
