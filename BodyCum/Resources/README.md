# Local rendering resources

This directory intentionally tracks documentation only. The development
plugin needs four `.bundle` files and `AccessoryJuiceMask.png`; those binary
assets are not included in this public source staging tree.

Read [resource provenance](../../docs/ASSET-PROVENANCE.md) and
[build instructions](../../docs/BUILD.md) before compiling.

To import the existing resource set for local development:

```powershell
pwsh -File .\Import-LocalResources.ps1 -PluginPath 'C:\path\to\BodyCum.dll'
```

Run this command from the repository root. The script checks the source DLL
and resource hashes against `resource-hashes.json` and does not overwrite a
different existing resource. Resource files remain ignored by Git.
