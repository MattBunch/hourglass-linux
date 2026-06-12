# Hourglass Linux Port Scaffold

This repository now contains an initial scaffold for a native Linux port of Hourglass. The existing Windows WPF application remains intact in the original `Hourglass.sln` and `Hourglass/` source tree.

## Requirements

- .NET 10 SDK
- Linux desktop runtime dependencies required by Avalonia
- Network access for first-time NuGet restore

The Linux scaffold is pinned to .NET SDK `10.0.108` in `global.json`:

```json
{
  "sdk": {
    "version": "10.0.108",
    "rollForward": "latestFeature"
  }
}
```

## Projects

- `src/Hourglass.Core`: future platform-neutral domain logic.
- `src/Hourglass.Platform`: future platform service interfaces.
- `src/Hourglass.Linux.Services`: future Linux-specific service implementations.
- `src/Hourglass.Linux.Avalonia`: placeholder native Linux UI shell.
- `tests/Hourglass.Core.Tests`: modern unit test project for core logic.

The Linux scaffold is in `Hourglass.Linux.sln`. The legacy Windows solution is still `Hourglass.sln`.

## Build And Test

```bash
dotnet --list-sdks
dotnet restore Hourglass.Linux.sln
dotnet build Hourglass.Linux.sln
dotnet test Hourglass.Linux.sln
dotnet run --project src/Hourglass.Linux.Avalonia/Hourglass.Linux.Avalonia.csproj
```

If the local environment blocks writes under the default .NET CLI home, use:

```bash
DOTNET_CLI_HOME=/tmp/hourglass-dotnet-home dotnet restore Hourglass.Linux.sln
DOTNET_CLI_HOME=/tmp/hourglass-dotnet-home dotnet build Hourglass.Linux.sln
DOTNET_CLI_HOME=/tmp/hourglass-dotnet-home dotnet test Hourglass.Linux.sln
```

## Not Implemented Yet

- Full Hourglass UI parity.
- Linux tray/status notifier support.
- Wake-from-suspend scheduling.
- Flatpak, AppImage, Snap, DEB, RPM, or AUR packaging.
- Full notifications.
- Session inhibition and keep-awake behavior.
- Audio alert playback.
- Settings migration.
- Replacement update mechanism.

This first scaffold is intentionally limited to architecture, documentation, buildable project structure once .NET 10 is installed, a placeholder Avalonia window, and a placeholder unit test.
