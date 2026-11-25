# Build BodyCum

[README](../README.md) · [한국어 설치 안내](INSTALL.ko.md)

Run the commands below from the repository root on Windows using [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows). The result is **`bin/BodyCum.dll`**, plugin version **1.0.0**.

## Required local inputs

The build references your installed Koikatsu CharaStudio files and plugins. Prepare a game installation with BepInEx 5, KKAPI 1.42.1 or newer, and Extended Save. See the [installation guide](INSTALL.ko.md) for dependency links.

Use a [.NET SDK](https://dotnet.microsoft.com/en-us/download) for the standard compiler route, or an existing Roslyn compiler as described below. The scripts do not use a `.csproj`; `dotnet build` is not the build command for this repository.

This source preparation intentionally excludes these five embedded resources pending provenance review:

```text
BodyCum/Resources/CustomJuiceColor.bundle
BodyCum/Resources/CustomJuiceColor.KKUTS_Tess.bundle
BodyCum/Resources/CustomJuiceColor.Hair.bundle
BodyCum/Resources/CustomJuiceColor.Skin.bundle
BodyCum/Resources/AccessoryJuiceMask.png
```

A fresh checkout cannot compile successfully until the required resources are supplied. Choose one of the two local preparation routes below. See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for resource information.

## Option 1: Import resources from a local BodyCum DLL

If you already have the recorded BodyCum 1.0 UI-cleanup `BodyCum.dll`, import its five resources into the source tree. The importer verifies the exact DLL and resource hashes in `resource-hashes.json`; it does not accept arbitrary BodyCum builds.

```powershell
pwsh -File .\Import-LocalResources.ps1 -PluginPath 'C:\LocalBuild\BodyCum.dll'
```

Use a local DLL you are authorized to use. This step prepares local build inputs; it does not publish them. After importing, run the normal compilation command:

```powershell
pwsh -File .\Build-BodyCum.ps1 -GamePath 'C:\Koikatsu'
```

With all four bundles present, the build uses those bundles unless `-RebuildShaders` is specified. The accessory PNG is embedded too. The script writes `bin/BodyCum.dll` and prints its SHA-256 hash.

## Option 2: Rebuild shaders from local assets

Without a local DLL to import, you need all of the following:

- Your installed Koikatsu **Unity 5.6.2f1** assets, including `abdata/chara/mm_base.unity3d`.
- **SB3Utility v23.1.7**, with its required DLLs and plugins. The script's default folder is `<game>/[MODDING] Tools/SB3Utility v23.1.7`.
- The installed **nHaruka KKUTS v2.3** zipmod containing `abdata/chara/kkutstess.unity3d`. [Creator's v2.3 download page](https://ux.getuploader.com/nHaruka_KK/download/150)
- An authorized local copy of the edited **`AccessoryJuiceMask.png`**, placed at `BodyCum/Resources/AccessoryJuiceMask.png`. Shader rebuilding does not generate this image, and it is not provided by this repository.
- The Windows .NET Framework compiler at `%WINDIR%/Microsoft.NET/Framework64/v4.0.30319/csc.exe`, used by the shader packing tools.

Then run:

```powershell
pwsh -File .\Build-BodyCum.ps1 -GamePath 'C:\Koikatsu' `
  -Sb3UtilityPath 'C:\Tools\SB3Utility v23.1.7' `
  -KkutsZip 'C:\Koikatsu\mods\local-KKUTS-v2.3.zipmod' `
  -RebuildShaders
```

Replace the example paths with your actual folders and zipmod. If `-KkutsZip` is omitted, the script searches the game's `mods` folder for exactly one filename matching `*KKUTS*v2.3*.zipmod`.

Missing bundles trigger their builders automatically, even without `-RebuildShaders`. Supplying `-RebuildShaders` rebuilds all four. Locally rebuilt bundles and DLLs are not claimed to be byte-identical to the existing 1.0.0 baseline.

## Compiler alternatives

The standard script discovers Roslyn from an installed .NET SDK. You can instead specify a Roslyn compiler directly:

```powershell
pwsh -File .\Build-BodyCum.ps1 -GamePath 'C:\Koikatsu' `
  -CompilerPath 'C:\Tools\Roslyn\csc.exe'
```

`-CompilerPath` also accepts a Roslyn `csc.dll`; that route runs it with `dotnet`.

### Use an already installed dnSpy Roslyn

The optional adapter was previously used with dnSpy v6.1.5's Roslyn libraries. After preparing the resources, run:

```powershell
pwsh -File .\Build-With-Installed-Roslyn.ps1 -GamePath 'C:\Koikatsu'
```

It defaults to `<game>/[MODDING] Tools/dnSpy v6.1.5/bin`. For another location:

```powershell
pwsh -File .\Build-With-Installed-Roslyn.ps1 -GamePath 'C:\Koikatsu' `
  -RoslynPath 'C:\Tools\dnSpy v6.1.5\bin'
```

This route uses the installed .NET Framework compiler to build a small local adapter, then invokes the same `Build-BodyCum.ps1`. It does not require installing a .NET SDK. Compiler libraries and generated helper executables are not source distribution inputs.

The wrapper does not expose `-Sb3UtilityPath` or `-KkutsZip`. If you need those overrides with the adapter, prepare it first and call the regular build script:

```powershell
pwsh -File .\BuildTools\Prepare-Compiler.ps1 -RoslynPath 'C:\Tools\dnSpy v6.1.5\bin'
pwsh -File .\Build-BodyCum.ps1 -GamePath 'C:\Koikatsu' `
  -CompilerPath '.\BuildTools\RoslynCscHost.exe' `
  -Sb3UtilityPath 'C:\Tools\SB3Utility v23.1.7' `
  -KkutsZip 'C:\Koikatsu\mods\local-KKUTS-v2.3.zipmod'
```

## Build results and verification

Only the built `BodyCum.dll` is needed for plugin installation. Place it in the game's `BepInEx/plugins` folder with Studio closed. It contains the shader bundles and mask image.

The existing final 1.0.0 baseline previously passed local compilation and static compatibility checks. Those checks did **not** include running that final baseline's native Studio UI, rendering, or complete scene save/reload behavior. A successful compilation of your local build does not establish those runtime results.
