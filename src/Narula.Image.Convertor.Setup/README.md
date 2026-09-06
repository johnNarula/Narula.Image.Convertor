# Narula.Image.Convertor.Setup

The Windows installer. It contains no code: it publishes `img2img.exe` and `img2imgUI.exe` into
one staging folder and hands that folder to Inno Setup.

## Building it

```
winget install JRSoftware.InnoSetup          # once; Inno Setup 6.3 or newer
dotnet build src/Narula.Image.Convertor.Setup -t:Installer
```

The result is `publish/installer/img2img-setup-<version>.exe`. The version is the solution's
own — `1.0.YY.MMDD`, dated when it was built — so the file name says when it was made.

Nothing else in the solution depends on this project, and a plain `dotnet build` does not run
it. A machine without Inno Setup builds and tests the rest of the repository normally.

## What it installs

| | |
|---|---|
| Files | `img2img.exe`, `img2imgUI.exe`, and `settings.json` if there isn't one already |
| Where | Per-user by default, with no administrator prompt; the wizard offers all-users |
| PATH | The install folder is added to **your** PATH, even for an all-users install, because that is the one a terminal inherits |
| Start menu | Always |
| Desktop | Offered, off by default |
| Explorer | "Convert images here..." on folders and on folder backgrounds, opening the window with that folder already filled in |
| .NET | Checks for the .NET 10 runtime and downloads it from Microsoft only if it is missing |

Uninstalling removes all of it, including the PATH entry and the right-click menu, but leaves
your own `%AppData%\9thAct\img2img\settings.json` alone.

## Things worth knowing

**It is not signed.** Windows SmartScreen will say the publisher is unknown, and someone
installing it has to choose *More info* then *Run anyway*. Signing needs a paid certificate;
there is no free Authenticode option that SmartScreen trusts, and a self-signed certificate
does not help.

**The Explorer entry calls `img2imgUI.exe` directly**, not `img2img.exe -ui`, because
`img2img.exe` is a console program and going through it would flash a console window.

**AppId must never change.** It is how Windows recognises an existing installation, so changing
it would leave two entries in Add/Remove Programs instead of upgrading the one that is there.
