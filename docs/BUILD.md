# Building BodyCum

Run these commands from the repository root in [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows). You'll need Koikatsu CharaStudio, [the runtime dependencies](INSTALL.md), and a [.NET SDK](https://dotnet.microsoft.com/en-us/download).

## Build with the 1.0 resources

Get `BodyCum.dll` from the [1.0 release](https://github.com/Naruu6/BodyCum/releases/tag/v1.0.0). The importer checks the DLL and its six embedded resources against [resource-hashes.json](../resource-hashes.json).

```powershell
pwsh -File .\Import-LocalResources.ps1 -PluginPath 'C:\Downloads\BodyCum.dll'
pwsh -File .\Build-BodyCum.ps1 -GamePath 'C:\Koikatsu'
```

Look for the result at `bin/BodyCum.dll`. Your local build can have a different checksum from the release file.

## Rebuild the shaders

Shader building uses the installed Koikatsu Unity 5.6.2f1 assets, nHaruka's KKUTS v2.3 zipmod, SB3Utility v23.1.7, and the Windows .NET Framework compiler. The shader scripts use the `HairDirectionRegions.png` lookup image from the import step above.

```powershell
pwsh -File .\Build-BodyCum.ps1 -GamePath 'C:\Koikatsu' `
  -Sb3UtilityPath 'C:\Tools\SB3Utility v23.1.7' `
  -KkutsZip 'C:\Koikatsu\mods\local-KKUTS-v2.3.zipmod' `
  -RebuildShaders
```

If you omit the tool and zipmod paths, the script looks under `[MODDING] Tools` and `mods` in your Koikatsu folder. The [resource notes](ASSET-PROVENANCE.md) explain where each bundle comes from.

## Use a different compiler

Pass a Roslyn `csc.exe` or `csc.dll` to `-CompilerPath`. If you already have dnSpy v6.1.5, you can use its Roslyn libraries through the included adapter.

```powershell
pwsh -File .\Build-With-Installed-Roslyn.ps1 -GamePath 'C:\Koikatsu'
```
