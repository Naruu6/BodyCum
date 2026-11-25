[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PluginPath
)

$ErrorActionPreference = 'Stop'
$pluginFile = (Resolve-Path -LiteralPath $PluginPath).Path
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'resource-hashes.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $pluginFile -Algorithm SHA256).Hash -ne $manifest.dllSha256) {
    throw 'This import recipe targets the recorded BodyCum 1.0 UI-cleanup DLL. The supplied DLL has a different hash.'
}

$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($pluginFile))
$destination = Join-Path $PSScriptRoot 'BodyCum\Resources'
$pending = @()
$sha = [Security.Cryptography.SHA256]::Create()
try {
    foreach ($resource in $manifest.resources) {
        $stream = $assembly.GetManifestResourceStream('BodyCum.Resources.' + $resource.name)
        if ($null -eq $stream) { throw ('Missing embedded resource: ' + $resource.name) }
        $memory = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            $bytes = $memory.ToArray()
        } finally {
            $memory.Dispose()
            $stream.Dispose()
        }
        $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
        if ($bytes.Length -ne $resource.bytes -or $hash -ne $resource.sha256) {
            throw ('Embedded resource does not match the recorded baseline: ' + $resource.name)
        }
        $target = Join-Path $destination $resource.name
        if (Test-Path -LiteralPath $target) {
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash) {
                throw ('Refusing to replace a different local resource: ' + $resource.name)
            }
        } else {
            $pending += [pscustomobject]@{ Path = $target; Bytes = $bytes }
        }
    }
} finally {
    $sha.Dispose()
}

$null = New-Item -ItemType Directory -Path $destination -Force
foreach ($item in $pending) {
    [IO.File]::WriteAllBytes($item.Path, $item.Bytes)
}
Write-Output ('Verified all five local resources; imported ' + $pending.Count + '. No files were uploaded.')
