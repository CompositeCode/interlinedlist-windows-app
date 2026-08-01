# InterlinedList — Document Synchronization Utility & Background Tray Service (Windows)

## Context

The InterlinedList Windows client (`interlinedlist-windows-app`, C#/WPF/.NET 10) is a
feature-broad *interactive* client, but documents live only inside the app — there is
no way to keep a local folder of `.md` files in sync so an external tool like
**Obsidian** can open, edit, and organize them. We want the same
"Dropbox/OneDrive-style" background sync that the sibling repo
[`CompositeCode/interlinedlist-synchronization`](https://github.com/CompositeCode/interlinedlist-synchronization)
already implements — and that repo **already ships a mature, MIT-licensed, ~165-test
Windows tray agent (`InterlinedSync`) on the identical C#/.NET/WPF stack**. Rather than
build from scratch, we **vendor that engine + tray into this repo**, unify it with this
app's auth/packaging, add **folder-tree mirroring** (the reference's one real gap), and
wire it so **installing the Windows client also installs and auto-starts the sync
utility** in the system tray.

Outcome: after install, a tray icon runs in the background at login, watches a local
folder (default `%USERPROFILE%\Documents\InterlinedList`), and keeps it bidirectionally
in sync with the user's InterlinedList documents — folders mirrored as subdirectories —
using the token the user already signed in with in the main app.

## Locked decisions

1. **Approach = Vendor + unify.** Port `InterlinedSync`'s engine + tray into two new
   in-repo projects; rewire it to this app's DPAPI token + API client; ship via the
   existing **WiX MSI + MSIX** tracks (not Inno Setup).
2. **Background model = login-launched tray** (per-user, autostart via HKCU `Run` /
   MSIX `StartupTask`). **No NT Windows Service** (session-0 can't show a tray icon or
   read the per-user DPAPI token / per-user folder).
3. **Folder layout = mirror the server folder tree** into real subdirectories, fully
   bidirectional (including moves between folders). This is net-new work on top of the
   vendored engine, which is flat today.

## "Menu bar" → Windows mapping

The request's macOS phrasing maps to Windows as: **menu bar → system tray
(notification area)**; **background service → login-launched background tray process**.

---

## Target architecture

```
interlinedlist-windows-app/
  InterlinedList/                         (existing WPF app — mostly untouched)
    Services/InterlinedApiClient.Documents.cs   (+ add ONE delta-sync method)
    Services/CredentialStore.cs                 (reused as-is: shared DPAPI token)
  InterlinedList.Sync.Core/               (NEW — platform-neutral engine library, net10.0)
    Sync/SyncEngine.cs                          (vendored: BackgroundService pull+push loops)
    Sync/SyncStateRepository.cs                 (vendored: SQLite state.db)
    Sync/ConflictResolver.cs                    (vendored: .conflict-<ts>.md)
    FileSystem/FileMapper.cs                     (vendored + EXTENDED for folder tree)
    FileSystem/FileSystemWatcherService.cs       (vendored: *.md watcher + debounce)
    Abstractions/ (IDocumentSyncClient, ICredentialSource, IFileMapper, ...)
    Adapters/ApiDocumentSyncClient.cs            (NEW: wraps InterlinedApiClient)
  InterlinedList.Sync/                    (NEW — tray host .exe, net10.0-windows, WinExe)
    App / DI generic host / TrayIconController / Settings + Onboarding windows
    Startup/RegistryAutoStartManager.cs          (vendored: HKCU Run)
  InterlinedList.Sync.Core.Tests/         (NEW — xUnit, folder-tree + engine tests)
  installer/ Package.wxs                   (EXTENDED: bundle sync exe + autostart)
  InterlinedList.Package/Package.appxmanifest (EXTENDED: 2nd app + windows.startupTask)
  .github/workflows/build.yml              (EXTENDED: build/test/publish sync projects)
```

### Reuse map (what we lift vs. what we replace)

| Vendored from `interlinedlist-synchronization/windows` (keep) | Replaced/adapted to unify with this repo |
| --- | --- |
| `SyncEngine` (pull loop + push channel consumer, `BackgroundService`) | — |
| `SyncStateRepository` + `state.db` schema (docs/folders/sync_metadata) | — (kept; extended for folder paths) |
| `ConflictResolver` (4-way truth table, conflict-copy naming) | — |
| `FileSystemWatcherService` (`*.md`, 500 ms debounce, atomic writes) | — |
| `FileMapper` (sanitization, reserved-name handling) | **Extended** for `folderId`→subdir mapping |
| `TrayIconController` / `TrayMenuBuilder` (Hardcodet.NotifyIcon.Wpf) | — |
| `RegistryAutoStartManager` (HKCU `Run`) | — |
| ~~`InterlinedListClient` (its own HTTP client)~~ | **Replace** with adapter over this repo's `InterlinedApiClient` |
| ~~`CredentialManager` (PasswordVault `com.interlinedlist.sync`)~~ | **Replace** with this repo's DPAPI `CredentialStore` (`session.dat`) |
| ~~Inno Setup installer~~ | **Replace** with existing WiX MSI + MSIX |

Because the reference is DI/interface-driven (`ICredentialStore`, `IInterlinedListClient`,
`IFileMapper`, `IFileWatcher`, `IConflictResolver`, `ISyncStateRepository`,
`IAutoStartManager`), swapping the two seams above is low-risk.

---

## Component design

### 1. Add the delta-sync client method (this repo's API layer)

The endpoint **exists server-side** (confirmed by the sibling repo's live-validated
`API_CONTRACT.md` and by the macOS client already calling it); this Windows client just
lacks a method. Add to `InterlinedList/Services/InterlinedApiClient.Documents.cs`:

```csharp
// GET /api/documents/sync[?lastSyncAt=<ISO-8601 "O">]
public Task<DocumentSyncResponse> GetDocumentSyncAsync(DateTimeOffset? lastSyncAt, CancellationToken ct = default);
```

New wire models (in `InterlinedList/Models/`), because the existing `DocumentSummary`
omits the tombstone field:

```csharp
record DocumentSyncResponse(DateTimeOffset? LastSyncAt, IReadOnlyList<DocumentDelta> Documents, IReadOnlyList<DocumentFolder> Folders);
record DocumentDelta(string Id, string Title, string? Content, string? FolderId,
                     DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt);  // DeletedAt != null ⇒ tombstone
```

**Push path reuses existing methods** (no `POST /documents/sync` batch needed — the
reference agent pushes via individual writes, which this repo already has):
`CreateDocumentAsync`, `UpdateDocumentAsync`, `DeleteDocumentAsync`,
`CreateDocumentFolderAsync`, `RenameDocumentFolderAsync`, `DeleteDocumentFolderAsync`,
`CreateDocumentInFolderAsync`.

### 2. Unify auth — one sign-in, shared DPAPI token

- The tray host resolves the bearer token via this repo's
  `CredentialStore.LoadToken()` (DPAPI `CurrentUser` scope → a separate process running
  as the same user can decrypt `%LocalAppData%\InterlinedList\session.dat`).
- Provide `ICredentialSource` in `Sync.Core` implemented by a tiny adapter over
  `CredentialStore` (both read/write the **same** `session.dat`), replacing the
  reference's PasswordVault store.
- **If no token is present** (utility launched before the user ever signed in), the tray
  shows a lightweight Onboarding window (ported from the reference `OnboardingViewModel`)
  that mints a sync-token via `POST /api/auth/sync-token` and writes it back through the
  **same** `CredentialStore.SaveToken()`, so main app and utility stay in lockstep.
- `ApiDocumentSyncClient` adapter wires `InterlinedApiClient.AccessToken` from the shared
  token and exposes `GetDocumentSyncAsync` + the push methods to the engine.

### 3. Folder-tree materialization (the net-new part)

The vendored `FileMapper` is flat (`syncFolder / sanitize(title).md`). Extend it and the
engine so `folderId` maps to real subdirectories, bidirectionally.

**State (`state.db`) additions:** ensure a `folders(id PK, parent_id, name, local_path)`
table; keep `documents(id, local_path, ...)`. Migration adds `local_path` to `folders`.

**Server → local:**
1. Apply folder deltas first: build the folder path for each folder by walking
   `parent_id` to root, sanitizing each segment; upsert `folders.local_path`;
   `Directory.CreateDirectory`. On a folder **rename/move** (name or parent changed),
   move the existing subtree (`Directory.Move`) and update descendant `local_path`s.
2. For each document delta: compute target path =
   `syncFolder / folders[doc.FolderId].local_path / sanitize(title).md`
   (root when `FolderId == null`). If the stored `local_path` differs (title rename or
   **folder change**), `File.Move` the existing file to the new path; else write content.
   Tombstone (`DeletedAt != null`) → delete the file + row.

**Local → server (via `FileSystemWatcher`):**
- File **created** under a subdirectory → resolve/create the server folder chain
  (`CreateDocumentFolderAsync` for any missing segment, cached in `folders`), then
  `CreateDocumentInFolderAsync` (or `CreateDocumentAsync` at root).
- File **moved** to a different subdirectory (rename event across dirs) → treat as
  **folder change**: `UpdateDocumentAsync` with the new `folderId` (create folder chain
  if needed). Filename-only rename → title update.
- File **modified** → `UpdateDocumentAsync` (SHA-256 dedupe as in the reference).
- File **deleted** → `DeleteDocumentAsync`.
- **Conservative folder-mutation policy for v1:** creating/renaming a subfolder locally
  is honored lazily (folders are created on the server when a document lands in them; a
  local folder rename is applied when a contained doc's move is detected). **Deleting a
  folder locally does NOT delete the server folder** (avoids destructive surprises from
  editor churn) — only file deletions delete documents. Document this in Settings help.

**Conflict handling unchanged:** server wins the canonical filename; the diverged local
copy is preserved as `<stem>.conflict-<yyyyMMddTHHmmss>.md` and excluded from the watch
scan (reference `ConflictResolver` + `FileMapper.GetConflictPath`).

**Deletion reconciliation:** delta tombstones are unreliable per `API_CONTRACT.md`, so
run a **full `GET /api/documents` + `GetDocumentFoldersAsync` reconcile every N cycles**
(e.g. every 20th poll, ~10 min at 30 s) to catch "seen-then-absent" deletions and
folder-tree drift definitively.

### 4. Tray host (`InterlinedList.Sync`)

- **Host:** `Microsoft.Extensions.Hosting` generic host; `SyncEngine` registered via
  `AddHostedService`; hosted inside a minimal WPF `App` (no main window; tray only).
- **Tray:** `Hardcodet.NotifyIcon.Wpf` `TaskbarIcon` driven by `TrayMenuBuilder`
  (declarative, unit-testable) → menu: **Sign in… / Signed in as {user}**, **Open Sync
  Folder**, **Sync Now**, **Pause/Resume**, **Settings**, **Exit**. Status icons
  (`tray-idle/syncing/paused/error`) swap on `SyncState` (Idle/Syncing/Paused/Error/
  SignedOut/Offline/AuthExpired). Reuse this repo's brand assets for the icons (Strata
  teal/amber) generated from `brand-kit/logo`.
- **Autostart toggle:** `RegistryAutoStartManager` writes
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\InterlinedListSync`. Installer
  sets it once at install; the Settings toggle manages it thereafter (single source of
  truth to avoid double-registration — installer only writes if the value is absent).
- **Settings window:** sync folder (`OpenFolderDialog`), poll interval (default 30 s,
  min 10 s), start-at-login toggle, notification toggles. Prefs JSON at
  `%APPDATA%\InterlinedList\sync\appsettings.json`; state at `...\sync\state.db`; logs at
  `%LOCALAPPDATA%\InterlinedList\sync\logs\` (Serilog, 7-day rolling).
- **Resilience (vendored):** 401→`AuthExpired` pause; offline pause via
  `NetworkChange.NetworkAvailabilityChanged`; 429 honored via `Retry-After` + jittered
  exponential backoff.

### 5. Packaging — install + autostart the utility with the app

**MSI (WiX `installer/Package.wxs` + `.wixproj`):**
- Publish the Sync tray exe alongside the app. Add a **second publish** of
  `InterlinedList.Sync.csproj` (self-contained `win-x64`) into a `sync\` subfolder of the
  app publish dir, then a new `<ComponentGroup Id="SyncFiles">` harvesting
  `$(PublishDir)\sync\**` into `INSTALLFOLDER\sync`.
- Autostart component (copy the existing `ApplicationShortcut` pattern, new GUID):
  ```xml
  <Component Id="SyncAutoStart" Directory="INSTALLFOLDER">
    <RegistryValue Root="HKCU"
      Key="Software\Microsoft\Windows\CurrentVersion\Run"
      Name="InterlinedListSync" Type="string"
      Value="[INSTALLFOLDER]sync\InterlinedList.Sync.exe" KeyPath="yes" />
  </Component>
  ```
  Add `SyncFiles` + `SyncAutoStart` to the main `<Feature>`. Optional Start-Menu
  shortcut "InterlinedList Sync".

**MSIX (`InterlinedList.Package`):**
- Add `<ProjectReference Include="..\InterlinedList.Sync\InterlinedList.Sync.csproj">` to
  the `.wapproj` (triggers nested publish → **the Sync csproj MUST declare
  `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>`** to avoid the known `NETSDK1047`,
  per CLAUDE.md).
- Add a second `<Application Id="InterlinedListSync" Executable="...InterlinedList.Sync.exe"
  EntryPoint="Windows.FullTrustApplication">` and a startup extension:
  ```xml
  <Extensions>
    <uap5:Extension Category="windows.startupTask" Executable="InterlinedList.Sync.exe" EntryPoint="Windows.FullTrustApplication">
      <uap5:StartupTask TaskId="InterlinedListSyncStartup" Enabled="true" DisplayName="InterlinedList Sync" />
    </uap5:Extension>
  </Extensions>
  ```
- Keep `TargetPlatformVersion` matching the runner's installed UAP SDK (CLAUDE.md
  constraint — do not bump blindly).

### 6. CI (`.github/workflows/build.yml`)
- `build-app`: also `dotnet build` `InterlinedList.Sync.Core` + `InterlinedList.Sync`
  and `dotnet test InterlinedList.Sync.Core.Tests`.
- `build-msi`: add a publish step for `InterlinedList.Sync.csproj` into `sync\` before
  the WiX build.
- `build-msix`: no change beyond the `ProjectReference` (nested publish handles it).
- Add `INTERLINEDLIST_EMAIL/_PASSWORD` secret-gated integration test job (skips when
  unset), mirroring the existing contract-test convention.

---

## Files to create / modify

**New projects**
- `InterlinedList.Sync.Core/` (net10.0 library): vendored `Sync/`, `FileSystem/`,
  `Abstractions/`, `Adapters/ApiDocumentSyncClient.cs`, credential adapter.
- `InterlinedList.Sync/` (net10.0-windows `WinExe`, `<RuntimeIdentifiers>win-x64`): tray
  host, DI host, tray/menu, Settings + Onboarding windows, `RegistryAutoStartManager`,
  brand tray icons.
- `InterlinedList.Sync.Core.Tests/` (xUnit + Moq + `System.IO.Abstractions.TestingHelpers`):
  port relevant reference tests **plus new folder-tree mapping/move/conflict tests**.

**Modified**
- `InterlinedList/Services/InterlinedApiClient.Documents.cs` — add `GetDocumentSyncAsync`.
- `InterlinedList/Models/` — add `DocumentSyncResponse`, `DocumentDelta`.
- `InterlinedList.slnx` — add the three new projects.
- `installer/Package.wxs` (+ `.wixproj`) — `SyncFiles` group + `SyncAutoStart` component.
- `InterlinedList.Package/Package.appxmanifest` (+ `.wapproj`) — 2nd app + startupTask +
  ProjectReference.
- `.github/workflows/build.yml` — build/test/publish the new projects.
- `CLAUDE.md` — document the new sync utility, its projects, and packaging seams.

**Dependencies added:** `Hardcodet.NotifyIcon.Wpf`, `Microsoft.Extensions.Hosting`,
`Microsoft.Data.Sqlite`, `Serilog`, `Polly` (matching the reference's set;
`CommunityToolkit.Mvvm` already present).

---

## Implementation phases

1. **Scaffold + engine (flat, end-to-end):** create the three projects; vendor the
   engine; write `ApiDocumentSyncClient` + `GetDocumentSyncAsync`; DPAPI credential
   adapter. Prove server↔local `.md` sync **flat** with the test account.
2. **Folder-tree materialization:** extend `FileMapper` + `SyncEngine` + `state.db` for
   `folderId`↔subdirectory, both directions incl. moves; conservative local-folder-delete
   policy; deletion reconciliation pass.
3. **Tray host UX:** Hardcodet tray + menu + status icons, Settings + Onboarding windows,
   autostart toggle, notifications.
4. **Packaging + CI:** WiX `SyncFiles`/`SyncAutoStart`, MSIX 2nd app + `startupTask`,
   `build.yml` updates.
5. **Tests, live verification, docs:** unit + integration tests; manual E2E on Windows;
   update `CLAUDE.md`.

---

## Verification

**Unit (cross-platform, run on macOS/CI):** `dotnet test InterlinedList.Sync.Core.Tests`
— cover `FileMapper` folder-path/sanitization/reserved-names; server→local folder create
/rename/move; local→server folder resolution + doc move-between-folders; conflict-copy
naming; tombstone + reconcile; SHA dedupe.

**Integration (env-gated, live API, test account from `.env`):** first-sync pulls all
docs into the mirrored tree; delta after edit; push after local edit; 429 backoff.
Skips when `INTERLINEDLIST_EMAIL/_PASSWORD` unset (matches existing contract-test policy).

**Manual E2E (must run on Windows — app is Windows-only):**
1. Build + install the MSI (or deploy the MSIX); confirm the tray icon appears at login
   and `HKCU\...\Run\InterlinedListSync` is set.
2. Sign in via the main app → confirm the tray shows "Signed in as {user}" (shared token,
   no second login).
3. On interlinedlist.com, create a doc inside a folder → it appears as
   `Documents\InterlinedList\<Folder>\<title>.md`.
4. Edit the file in **Obsidian** → change appears on the server; move it to another
   subfolder in Obsidian → the doc's `folderId` updates server-side.
5. Force a conflict (edit both sides between polls) → server copy wins; a
   `<stem>.conflict-<ts>.md` is written locally.
6. Delete a doc on the server → the local file disappears within a reconcile cycle.
7. `dotnet publish` the app self-contained `win-x64` still succeeds (what MSI harvests).

---

## Risks & edge cases
- **Unreliable delta tombstones** → mitigated by the periodic full reconcile.
- **Local folder deletes** intentionally do NOT cascade to server folder deletion in v1
  (destructive-churn guard) — called out in Settings/help.
- **Two independent API consumers** (main app view + tray engine) is fine — bearer auth
  is stateless; they keep separate `state.db`/UI and never contend (no shared SQLite).
- **.NET retarget** net9→net10 is trivial; package versions to match.
- **MSIX nested publish** needs `<RuntimeIdentifiers>win-x64` on the Sync csproj
  (NETSDK1047) and a `TargetPlatformVersion` matching the runner's UAP SDK.
- **Cross-platform authoring:** keep `Sync.Core` platform-neutral (net10.0) so it builds
  /tests on macOS; the tray host (WPF/Hardcodet) is Windows-target only (as today).

## Out of scope (fast-follows)
- Real Windows toast notifications (reference stubs them) — start with tray tooltip + log.
- Non-document sync (lists/orgs/feed) — the utility is documents-only by design.
- Offline write queue beyond the in-memory push channel.
- Code-signing certs (MSI/MSIX ship unsigned; Partner Center signs MSIX on ingestion).
