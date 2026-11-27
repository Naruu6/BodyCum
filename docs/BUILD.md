# Build BodyCum

Build on Windows with [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows), an installed Koikatsu CharaStudio, the [runtime dependencies](INSTALL.md#requirements), and a [.NET SDK](https://dotnet.microsoft.com/en-us/download). Run the commands from the repository root.

## Build with the release resources

Download `BodyCum.dll` from the [1.0 release](https://github.com/Naruu6/BodyCum/releases/tag/v1.0.0). The importer checks the DLL and each embedded resource against [resource-hashes.json](../resource-hashes.json).

```powershell
pwsh -File .\Import-LocalResources.ps1 -PluginPath 'C:\Downloads\BodyCum.dll'
pwsh -File .\Build-BodyCum.ps1 -GamePath 'C:\Koikatsu'
```

The build writes `bin/BodyCum.dll`, a standalone BepInEx plugin with the six rendering resources embedded. A local build can have a different DLL checksum from the distributed build.

## Rebuild shaders using local assets

The shader builders use installed Koikatsu Unity 5.6.2f1 assets, the nHaruka KKUTS v2.3 zipmod, SB3Utility v23.1.7, and the Windows .NET Framework compiler. Import the resources above first to obtain `BodyCum/Resources/HairDirectionRegions.png`; the shader builders use the existing lookup image rather than generating it.

```powershell
pwsh -File .\Build-BodyCum.ps1 -GamePath 'C:\Koikatsu' `
  -Sb3UtilityPath 'C:\Tools\SB3Utility v23.1.7' `
  -KkutsZip 'C:\Koikatsu\mods\local-KKUTS-v2.3.zipmod' `
  -RebuildShaders
```

The script defaults to the SB3Utility folder under `[MODDING] Tools` and looks for an installed KKUTS v2.3 zipmod when no paths are provided. See [Rendering sources](ASSET-PROVENANCE.md) for the input behind each resource.

## Use an existing Roslyn compiler

Set `-CompilerPath` to a Roslyn `csc.exe` or `csc.dll` to select a compiler directly. To use the Roslyn libraries included in a local dnSpy v6.1.5 installation, run the included adapter.

```powershell
pwsh -File .\Build-With-Installed-Roslyn.ps1 -GamePath 'C:\Koikatsu'
```
