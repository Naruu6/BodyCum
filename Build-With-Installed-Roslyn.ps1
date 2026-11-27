[CmdletBinding()]
param(
    [string]$GamePath = 'C:\Koikatsu',
    [string]$RoslynPath,
    [string]$FrameworkCompilerPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin')
)
$ErrorActionPreference = 'Stop'
if (!$RoslynPath) { $RoslynPath = Join-Path $GamePath '[MODDING] Tools\dnSpy v6.1.5\bin' }
$tools = Join-Path $PSScriptRoot 'BuildTools'
& (Join-Path $tools 'Prepare-Compiler.ps1') -RoslynPath $RoslynPath -FrameworkCompilerPath $FrameworkCompilerPath
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the installed Roslyn compiler adapter.' }
& (Join-Path $PSScriptRoot 'Build-BodyCum.ps1') -GamePath $GamePath -CompilerPath (Join-Path $tools 'RoslynCscHost.exe') -OutputDirectory $OutputDirectory
