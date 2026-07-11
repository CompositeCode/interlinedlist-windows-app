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
  App.xaml / App.xaml.cs        (Entry point: theme switching + login/session orchestration)
  LoginWindow.xaml / .xaml.cs   (Email/password gate, shown when no session restores)
  MainWindow.xaml               (Shell: custom title bar, left nav, center ContentControl, right rail)
  MainWindow.xaml.cs            (Code-behind: clock, nav → view switching, window chrome)
  Resources/
    Palette.xaml                (Invariant brand colors)
    Theme.Light.xaml            (Light semantic brushes)
    Theme.Dark.xaml             (Dark semantic brushes)
  Models/                       (Wire types matching the real API JSON — see API integration below)
  Services/
    ApiConfig.cs                 (Base URL: https://interlinedlist.com/)
    InterlinedApiClient.cs        (Core: HTTP/JSON plumbing + auth/user/messages/dig/notifications)
    InterlinedApiClient.Lists.cs         (partial class: Lists domain)
    InterlinedApiClient.Documents.cs     (partial class: Documents domain)
    InterlinedApiClient.Organizations.cs (partial class: Organizations domain)
    InterlinedApiClient.Search.cs        (partial class: cross-resource search)
    InterlinedApiClient.CrossPost.cs     (partial class: linked-identity/OAuth helpers)
    InterlinedApiException.cs
    CredentialStore.cs            (DPAPI-encrypted sync-token persistence)
    SessionService.cs             (login/logout/restore, exposes CurrentUser)
    AppServices.cs                 (process-lifetime singletons: Api, Session)
  ViewModels/                    (CommunityToolkit.Mvvm ObservableObject + [RelayCommand])
    LoginViewModel.cs, FeedViewModel.cs, MessageItemViewModel.cs,
    NotificationsViewModel.cs, NotificationItemViewModel.cs, ProfileSummaryViewModel.cs,
    ListsViewModel.cs, DocumentsViewModel.cs, OrganizationsViewModel.cs,
    SearchViewModel.cs, ConnectedAccountsViewModel.cs
  Views/                         (Self-contained UserControls; each owns its ViewModel —
                                   `DataContext = new XyzViewModel(AppServices.Session)` in its
                                   own constructor, not injected by MainWindow)
    FeedView, ListsView, DocumentsView, OrganizationsView, SearchView, ConnectedAccountsView
installer/
  InterlinedList.Installer.wixproj  (WiX v5 SDK project → classic .msi)
  Package.wxs                       (product/feature/shortcut definition)
  License.rtf                       (placeholder EULA for WixUI_Minimal)
InterlinedList.Package/
  InterlinedList.Package.wapproj    (Windows Application Packaging Project → MSIX)
  Package.appxmanifest              (Store identity, visual elements, capabilities)
  Images/                           (tile/splash assets generated from brand-kit logo)
```

## API integration

The app talks to the real InterlinedList backend at `https://interlinedlist.com`
(154-endpoint REST API, OpenAPI spec at `/api/openapi.json`) — there is no mock
data layer. Auth is a long-lived bearer token from `POST /api/auth/sync-token`
(the same mechanism the `il-sync` CLI and other native clients use — no cookie
jar), persisted DPAPI-encrypted via `CredentialStore`. **There is no
server-side revoke endpoint for this token** — treat `%LocalAppData%\InterlinedList\session.dat`
as a standing credential.

Covered now: login/session restore, paginated feed, compose, Dig/Undig,
notifications tray, profile/follow-counts rail, **Lists** (browse/create/
delete, freeform JSON data rows — no schema/column editor, see below),
**Documents** (personal markdown notes: root docs, folders, templates,
create/edit/delete), **Organizations** (browse orgs you belong to + the
public directory, create — **no member management**, see below), unified
**Search** (messages/people/lists/documents from one box), and **Connected
Accounts** (Bluesky/Mastodon/LinkedIn/Twitter linking + compose-time
cross-post toggles). Still not built: Stripe billing, replies/threads,
register/forgot-password, GitHub issue sync, per-list schema/column
definitions, LinkedIn per-page posting targets.

**Two real, load-bearing constraints discovered by live-probing the API — don't
"fix" these without re-verifying, they're not bugs in this app:**

1. **Not every endpoint accepts the bearer sync-token.** `GET/POST
   /api/organizations/{id}/members` and `GET /api/linkedin/targets` /
   `posting-targets` return `401` even with a valid token — that subsystem
   requires cookie-session auth this native client doesn't have. This is why
   Organizations has no member-management UI and Connected Accounts has no
   LinkedIn per-page targeting.
2. **The per-provider `GET /api/auth/{provider}/status` endpoints are a red
   herring** — they report whether the *server* has that OAuth integration
   configured, not whether *this user* has linked it. The real per-user link
   state is `GET /api/user/identities` (works fine with the bearer token),
   which is what `ConnectedAccountsViewModel` actually uses.

**Write-path caution, same pattern throughout:** `PostMessageAsync`/
`DigAsync`/`UndigAsync`/`AddListRowAsync`/`CreateOrganizationAsync`/
`RemoveIdentityAsync` don't parse their response bodies — some were
deliberately never exercised live (creating an org, disconnecting a linked
identity) to avoid mutating shared test infrastructure, so callers re-fetch
from a `GET` afterward rather than trusting a typed write response. Lists'
schema/column DSL (`PUT /api/lists/{id}/schema`) was only partially reverse
engineered and is **not implemented** — data rows work fine schema-less
(confirmed live), so that's the supported path. If you pick up any of this,
verify the actual response shape against a real (test) account before typing
it strictly, and prefer read-after-write over trusting an unverified envelope.

Left nav maps to real views now: **Feed**, **Lists**, **Documents**,
**Organizations**, **Search** (`MainWindow.xaml.cs` `NavItem_Click` swaps a
`ContentControl` via a small per-tag cache in `_views`), **Accounts**
(Connected Accounts), and **Alerts** (unchanged from Phase 2 — still a
right-rail toggle, not a center view).

## Packaging & distribution

Two independent, parallel packaging tracks — both wrap the same
`InterlinedList/InterlinedList.csproj` build, neither depends on the other:

**MSI (`installer/`)** — WiX Toolset v5 (SDK-style, NuGet-restored via
`WixToolset.Sdk`). It's a **two-step build, not one**: publish the app first,
then build the installer —

```sh
dotnet publish InterlinedList/InterlinedList.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false
dotnet build installer/InterlinedList.Installer.wixproj -c Release
```

WiX then harvests everything under
`InterlinedList/bin/Release/net10.0-windows/win-x64/publish/**` into the MSI via
a `<Files Include>` glob (no manual harvesting/heat step). **A single-command
`BeforeTargets="Build"` auto-publish target was tried and removed** — WiX's
file harvesting runs before that hook ever fires, confirmed empirically in CI
(the publish directory was still missing when harvesting ran), so don't
reintroduce that pattern without verifying it actually executes. Produces
`InterlinedList-Setup.msi` for direct download/side-loading, Start Menu
shortcut, per-machine install under Program Files. **Before shipping:**
replace `installer/License.rtf` with the real EULA.

**MSIX (`InterlinedList.Package/`)** — classic Desktop Bridge "Windows
Application Packaging Project" (`.wapproj`), the standard route for putting
an existing Win32/.NET desktop app into the Microsoft Store or sideloaded
MSIX. This project type is **not** `dotnet build`-able — its targets come
from `Microsoft.DesktopBridge.props/.targets`, installed with Visual Studio's
"Universal Windows Platform development" workload, not a NuGet package. It
builds fine headlessly with classic MSBuild once that workload is present
(confirmed in CI — see below); you don't need the VS IDE itself, just its
installed build tools. Command (matches what CI runs):

```powershell
msbuild InterlinedList.Package/InterlinedList.Package.wapproj /restore `
  /p:Configuration=Release /p:Platform=x64 /p:AppxBundlePlatforms=x64 `
  /p:AppxBundle=Always /p:UapAppxPackageBuildMode=StoreUpload
```

`UapAppxPackageBuildMode=StoreUpload` produces an unsigned `.msixupload`
bundle meant to be uploaded directly to Partner Center — Partner Center signs
it during ingestion, so **no code-signing certificate is needed for Store
submission** (you would need one for direct sideloading instead, a different
`UapAppxPackageBuildMode`). **Before Store submission:** replace the
placeholder `Publisher` value in `Package.appxmanifest` with the identity
reserved in Partner Center (VS's "Associate App with the Store" wizard will
rewrite `Identity`/`Properties` for you if you do it from the IDE instead).

**`TargetPlatformVersion` must match a UAP SDK actually installed on the
build machine** — this isn't a fixed "latest is fine" choice. Different
machines (and different GitHub Actions runner image versions over time) have
different SDKs installed; check what's present rather than assuming
(`Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Platforms\UAP"`) if a
future SDK-not-found error (`APPX3217`) shows up after a runner image update.

Image assets in `InterlinedList.Package/Images/` were generated from
`brand-kit/logo/logo-icon-master.png` with transparent padding (tiles) and a
teal-deep `#0C2C3A` background (splash) — regenerate them the same way if the
mark changes, don't hand-edit the PNGs.

## Continuous integration

`.github/workflows/build.yml` runs on every push/PR to `main`/`dev` (and via
manual `workflow_dispatch`), on `windows-latest`, with three jobs:

- **`build-app`** — fast sanity-check `dotnet build` of the WPF app alone; the
  other two jobs `needs:` this one so a trivial compile break fails fast
  instead of waiting on a much slower packaging build.
- **`build-msi`** — publishes the app, then builds the WiX MSI, uploads
  `InterlinedList-Setup-msi` as a workflow artifact.
- **`build-msix`** — adds `microsoft/setup-msbuild` (locates VS's MSBuild)
  then builds the `.wapproj` directly (see above), uploads
  `InterlinedList-Store-package` (the AppxBundle + `.msixupload`) as an
  artifact.

Both packaging jobs were debugged against real CI runs, not assumptions —
three real, non-obvious issues surfaced and are fixed in the current state
(don't reintroduce them):
1. `Package.wxs` declared `ARPNOMODIFY` itself, which collides with the same
   property already set by the `WixUI_Minimal` wixlib (`WIX0091` duplicate
   symbol) — removed.
2. The app project needs `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`
   declared (not just passed via `-r win-x64` on the CLI) — the MSIX
   packaging project triggers a *nested* publish of it as a `ProjectReference`
   that needs the RID available at restore time (`NETSDK1047` otherwise).
3. `TargetPlatformVersion` in the `.wapproj` must match an SDK actually
   installed on the runner (see above) — it drifts as GitHub updates runner
   images, so a future image update could reintroduce this failure.

## Windows-specific rules

- App icon: `brand-kit/icons/windows/InterlinedList.ico` (set via ApplicationIcon in .csproj)
- Title bar: `WindowChrome` (custom chrome, native resize); deep-teal `#0C2C3A`
- Window controls: right-aligned min / max / close; close highlights red on hover
- Card corners: 4px (`CornerRadius="4"`)
- Post card left edge: 4px wide; teal by default, amber once you've Dug that message (there's no server-side "stream type" to color by — see API integration)
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
