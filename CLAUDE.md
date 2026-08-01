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
    AppLog.cs                     (file + Windows Event Log diagnostics; wired to global
                                   crash handlers in App.xaml.cs — see Logging below)
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
InterlinedList.Sync.Core/          (net10.0, PLATFORM-NEUTRAL — builds & unit-tests on macOS)
  SyncEngine.cs                     (bidirectional pull/push; folder-tree materialization)
  SqliteSyncStateStore.cs           (SQLite state.db: doc/folder↔path ledger + delta cursor)
  FileMapper.cs / PathSanitizer.cs  (folderId→subdir mapping, filename safety, conflict paths)
  ConflictResolver.cs               (server-wins truth table → .conflict-<ts>.md)
  HttpDocumentSyncClient.cs         (the ~7 documents endpoints, incl. GET /api/documents/sync)
  Abstractions.cs / Models.cs       (IDocumentSyncClient, ICredentialSource, ISyncStateStore, DTOs)
InterlinedList.Sync/               (net10.0-windows WinExe — the system-tray background agent)
  Program.cs / TrayApplication.cs   (Hardcodet NotifyIcon tray, menu, no main window)
  SyncCoordinator.cs / FolderWatcher.cs (poll loop + FileSystemWatcher, all engine calls gated)
  DpapiCredentialSource.cs          (reads the SAME session.dat the main app writes)
  AutoStartManager.cs / SyncLog.cs  (HKCU Run toggle; file + Event Log diagnostics)
  SignInWindow.cs / SettingsWindow.cs (code-only WPF dialogs)
InterlinedList.Sync.Core.Tests/    (xUnit — 38 tests, run on macOS/CI)
installer/
  InterlinedList.Installer.wixproj  (WiX v5 SDK project → classic .msi)
  Package.wxs                       (product/feature/shortcut definition)
  License.rtf                       (placeholder EULA for WixUI_Minimal)
InterlinedList.Package/
  InterlinedList.Package.wapproj    (Windows Application Packaging Project → MSIX)
  Package.appxmanifest              (Store identity, visual elements, capabilities)
  Images/                           (tile/splash assets generated from brand-kit logo)
```

## Document synchronization utility (Obsidian sync)

A background **system-tray** utility (`InterlinedList.Sync`) keeps a local folder of
`.md` files bidirectionally in sync with the user's InterlinedList **documents**, so a
tool like **Obsidian** can edit them. It's installed and auto-started *with* the main
app (see Packaging). Design decisions and the full plan live in `synch-plan.md`.

- **Two projects, deliberate split.** `InterlinedList.Sync.Core` is `net10.0`
  **platform-neutral** (no WPF, no DPAPI) so the engine builds and unit-tests on
  macOS/CI. `InterlinedList.Sync` is the `net10.0-windows` WPF tray host that owns all
  Windows-only concerns (tray UI, DPAPI token read, `FileSystemWatcher`, HKCU autostart).
- **Vendored-from, not referenced.** The engine's design is ported from the sibling
  repo `CompositeCode/interlinedlist-synchronization` (`windows/`, MIT), but rebuilt to
  this repo's conventions and extended with **folder-tree mirroring** (that repo is flat).
  It does **not** reference the WPF app — it has its own minimal `HttpDocumentSyncClient`.
- **Unified auth = shared token, not shared code.** The utility reads the SAME
  DPAPI-encrypted `%LocalAppData%\InterlinedList\session.dat` the main app writes
  (`DpapiCredentialSource`), so signing in once in either place works for both. Its own
  sign-in (`AuthClient`) writes back to that same file.
- **Sync model.** Pull = `GET /api/documents/sync?lastSyncAt=` delta; folders become real
  subdirectories; server wins conflicts (local kept as `<stem>.conflict-<ts>.md`). Push =
  `FileSystemWatcher` (500ms debounce) → create/update/delete, creating server folders on
  demand. A **full-snapshot reconcile every 20th poll** covers the API's unreliable delete
  tombstones. Its own SQLite `state.db` (not the app's — separate process) is the ledger;
  **all engine calls are serialized through one gate** (the SQLite connection isn't
  thread-safe). **Load-bearing risk:** moving a doc between folders pushes `folderId` on
  `PATCH /api/documents/{id}`, which is **not yet live-verified** — see `synch-plan.md`.
- **Not a Windows Service** — a per-user login-launched tray process (session-0 services
  can't show a tray icon or read the per-user DPAPI token).

## Logging & diagnostics

Both processes log to disk and best-effort to the **Windows Event Log** (Event Viewer →
Windows Logs → Application), so an installed build that fails to start is diagnosable
rather than silent:

- **Main app:** `Services/AppLog.cs` → `%LocalAppData%\InterlinedList\logs\app-*.log`,
  Event Log source `InterlinedList`. `App.xaml.cs` installs global handlers
  (`DispatcherUnhandledException`, `AppDomain.UnhandledException`,
  `TaskScheduler.UnobservedTaskException`), wraps `OnStartup`, and shows a dialog pointing
  at the log on a startup failure. **This fixed a real "installs but won't run" crash:**
  `SessionService.TryRestoreSessionAsync` only caught `InterlinedApiException`, so a
  network/timeout/JSON error while validating a saved token escaped the `async void`
  `OnStartup` and killed the process before any window showed — now caught, logged, and it
  falls back to the login window.
- **Sync utility:** `SyncLog.cs` → `%LocalAppData%\InterlinedList\sync\logs\sync-*.log`,
  Event Log source `InterlinedListSync`.
- **Event Log sources are created by the MSI** (elevated); the apps run `asInvoker` and
  can only *write* to an existing source, so file logging is the always-available fallback.

## API integration

The app talks to the real InterlinedList backend at `https://interlinedlist.com`
(154-endpoint REST API, OpenAPI spec at `/api/openapi.json`) — there is no mock
data layer. Auth is a long-lived bearer token from `POST /api/auth/sync-token`
(the same mechanism the `il-sync` CLI and other native clients use — no cookie
jar), persisted DPAPI-encrypted via `CredentialStore`. The token is
long-lived, so treat `%LocalAppData%\InterlinedList\session.dat` as a standing
credential — but note the server **does** expose session management:
`GET /api/user/sessions` lists a user's active sync tokens and
`DELETE /api/user/sessions/{id}` revokes one (both accept the bearer token —
verified live 2026-07-31). An earlier revision of this file claimed no revoke
endpoint existed; that is no longer true.

Covered now (greatly expanded in the 2026-07-31 parity build-out):
login/session restore, paginated **feed** with compose (text + **image
attachments**, cross-post toggles), Dig/Undig, **replies/threads**, **edit/
delete** own posts, **report** posts, and **click-through to author profiles**;
notifications tray with **mark-one-read / delete-one / mark-all**; **Direct
Messages** (recipient list + thread + send); **People** (profile lookup,
follow/unfollow, follow-request approve/reject, a user's messages,
**block/mute/report**); **Lists** (browse/create/delete, freeform JSON data
rows with **row edit + delete** — no schema/column editor, see below);
**Documents** (root docs, templates, create/edit/delete + **folder CRUD /
new-doc-in-folder**); **Organizations** (browse + create + **full member
management**: add via search, change role, remove, edit/delete org);
**Settings** (profile edit, avatar-from-URL, email change, notification
preferences, blocked/muted management, **API-session list + revoke**, **CSV
data export**); unified **Search**; and **Connected Accounts** (Bluesky/
Mastodon/LinkedIn/Twitter linking + cross-post toggles). Still not built:
Stripe billing UI, register/forgot-password, GitHub issue sync (endpoints work
but the test account has no GitHub linked), per-list schema/column definitions,
LinkedIn per-page posting targets, scheduled-post UI (the service supports
`scheduledAt`), media *video* upload, list watchers/sharing, document sharing/
collaborators, Materialize ("Create from…"), and account deletion UI (the
service method exists, intentionally unsurfaced).

**Load-bearing constraints discovered by live-probing the API — don't
"fix" these without re-verifying, they're not bugs in this app:**

1. **A few endpoints only accept cookie-session auth, not the bearer
   sync-token.** Re-probed live 2026-07-31 with the test account:
   `GET /api/user/engagement` and `GET/PUT /api/user/dashboard-layout` return
   `401` with a valid bearer token (Stripe billing + some `/api/auth/*` session
   flows are the same shape). A native bearer-token client structurally can't
   get a cookie session, so those are either browser-handoff (like OAuth) or
   out of scope. **Correction to an earlier claim:** `GET
   /api/organizations/{id}/members`, `GET /api/linkedin/targets`, and
   `GET /api/linkedin/posting-targets` were *previously* documented here as
   `401`-walled, but as of 2026-07-31 they return `200` with the bearer token —
   member-management and LinkedIn per-page targeting **are** buildable now.
   (Member *mutations* — POST/PUT/DELETE — still need live write-verification.)
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

Left nav maps to real views now: **Feed**, **Messages** (Direct Messages),
**Lists**, **Documents**, **Organizations**, **People** (profiles + follow),
**Search** (`MainWindow.xaml.cs` `NavItem_Click` swaps a `ContentControl` via a
small per-tag cache in `_views`), **Accounts** (Connected Accounts),
**Settings**, and **Alerts** (right-rail toggle, not a center view). Feed/search
cards open a profile in the People tab via the `Navigator` hub
(`Services/Navigator.cs`) → `MainWindow.OpenProfile`.

## Packaging & distribution

Two independent, parallel packaging tracks — both wrap the same
`InterlinedList/InterlinedList.csproj` build, neither depends on the other:

**MSI (`installer/`)** — WiX Toolset v5 (SDK-style, NuGet-restored via
`WixToolset.Sdk`). It's a **two-step build, not one**: publish the app first,
then build the installer —

```sh
dotnet publish InterlinedList/InterlinedList.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false
# the sync tray utility publishes into the SAME payload folder (shares the runtime):
dotnet publish InterlinedList.Sync/InterlinedList.Sync.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -o InterlinedList/bin/Release/net10.0-windows/win-x64/publish
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
shortcuts (app **and** sync utility), per-machine install under Program Files.
Because the sync utility publishes into the same folder, the recursive glob picks
up `InterlinedList.Sync.exe` automatically; `Package.wxs` adds three registry-only
components: a **HKCU `Run` autostart** for the sync utility and two **Event Log
sources** (`InterlinedList`, `InterlinedListSync`) — the elevated install creates
them so the `asInvoker` apps can write to Event Viewer. **Before shipping:**
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

The `.wapproj` references **both** `InterlinedList.csproj` and
`InterlinedList.Sync.csproj`, so the MSIX bundles both executables. The manifest
declares a second `<Application Id="InterlinedListSync">` plus a
`windows.startupTask` extension (the `uap5` namespace; the MSIX equivalent of the
MSI's `Run` key) that auto-launches the sync utility at login. The Sync project
declares `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` for the same
nested-publish reason the main app does (`NETSDK1047`).

## Continuous integration

`.github/workflows/build.yml` runs on every push/PR to `main`/`dev` (and via
manual `workflow_dispatch`), on `windows-latest`, with three jobs:

- **`build-app`** — fast sanity-check `dotnet build` of the WPF app, plus the
  sync engine + tray utility, and `dotnet test` of `InterlinedList.Sync.Core.Tests`
  (the engine is platform-neutral so its tests run here); the other two jobs
  `needs:` this one so a trivial compile break fails fast instead of waiting on a
  much slower packaging build.
- **`build-msi`** — publishes the app **and the sync utility** (into the same
  payload folder), then builds the WiX MSI, uploads `InterlinedList-Setup-msi` as
  a workflow artifact.
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

**Releases** — `.github/workflows/release.yml` triggers on a pushed `v*` tag
(e.g. `git tag v1.0.0 && git push origin v1.0.0`). It runs the same
publish → WiX MSI build as CI, then attaches `InterlinedList-Setup.msi` to a
GitHub Release for that tag (auto-generated notes). Keep the tag version in
sync with `installer/Package.wxs` `Version` and `Package.appxmanifest`
`Version` (both `1.0.0.0` today) — bump all three together for a new release.

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
