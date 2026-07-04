# InterlinedList — Windows App

## Platform

C# / WPF targeting **Windows 10 or newer** (.NET 10, `net10.0-windows`).
Built with Visual Studio 2022 on Windows; code can be authored on macOS via
`dotnet` CLI + VS Code (C# Dev Kit), but the app can only build and run on Windows.

## Brand: Strata

InterlinedList uses the **Strata** design system: teal `#184860` structure,
green `#2FA877` actions, amber `#F0A830` for live/Dig, soft-sand light `#F4EEE2`
and near-black dark `#121317`. Type: Space Grotesk (display) / Manrope (body) /
JetBrains Mono (time & meta). Sharp corners (3-4px), 4pt spacing grid.
Dark mode follows the OS.
**Never introduce colors, fonts, or radii outside the tokens.**

- `brand-kit/theme/tokens.json` — source of truth for all design values
- `brand-kit/guidelines/brand-guidelines.html` — visual reference
- `InterlinedList/Resources/Palette.xaml` — brand palette ResourceDictionary (derived from tokens.json)
- `InterlinedList/Resources/Theme.Light.xaml` — light semantic brushes
- `InterlinedList/Resources/Theme.Dark.xaml` — dark semantic brushes

## Architecture

```
InterlinedList.slnx
InterlinedList/
  InterlinedList.csproj         (net10.0-windows, UseWPF=true)
  app.manifest                  (Windows 10+ DPI awareness + UAC)
  App.xaml / App.xaml.cs        (App entry point, theme switching)
  MainWindow.xaml               (Shell: custom title bar, nav, feed, right rail)
  MainWindow.xaml.cs            (Code-behind: clock, nav, window chrome)
  Resources/
    Palette.xaml                (Invariant brand colors)
    Theme.Light.xaml            (Light semantic brushes)
    Theme.Dark.xaml             (Dark semantic brushes)
  Models/
    Post.cs                     (Post data model)
```

## Windows-specific rules

- App icon: `brand-kit/icons/windows/InterlinedList.ico` (set via ApplicationIcon in .csproj)
- Title bar: `WindowChrome` (custom chrome, native resize); deep-teal `#0C2C3A`
- Window controls: right-aligned min / max / close; close highlights red on hover
- Card corners: 4px (`CornerRadius="4"`)
- Post card left edge: 4px wide, colored by stream type (teal / green / amber)
- Dark mode: read from HKCU registry at launch + listen via `SystemEvents.UserPreferenceChanged`

## Developing on macOS

`EnableWindowsTargeting` is set to `true` in the csproj to allow `dotnet build`
cross-compilation checks. The output still targets Windows only and must be run
on a Windows 10+ machine or VM.

## Running (Windows)

Open `InterlinedList.slnx` in Visual Studio 2022 and press F5, or:

```sh
dotnet build InterlinedList/InterlinedList.csproj -r win-x64
dotnet run --project InterlinedList/InterlinedList.csproj -r win-x64
```
