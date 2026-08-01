# The Gaps — InterlinedList Windows App vs. interlinedlist.com

> **Generated:** 2026-07-31 · **Method:** compared the app's implemented API
> surface (`InterlinedList/Services/InterlinedApiClient*.cs`) against the live
> OpenAPI 3.1 spec (`https://interlinedlist.com/api/openapi.json` — **189 paths,
> 244 operations**) and the product's own help docs (`/help`, `/help/api`).
>
> **Headline:** the desktop client implements ~**32** endpoints. The web
> product exposes on the order of **~190 user-facing operations** (excluding
> cron/webhooks/admin/internal plumbing). Feature parity is therefore roughly
> **15–20 %** by endpoint count — the app is a solid *read-mostly* client for a
> handful of domains (Feed, Lists, Documents, Orgs browse, Search, Connected
> Accounts) and is missing several whole product pillars: **Direct Messages,
> the social graph (follow/unfollow), People/profiles, Moderation, Account &
> Settings, replies/threads, media, exports, GitHub, and billing.**

---

## Progress — Session 1 (2026-07-31)

Live-probed the API with the `.env` test account, then built a large parity
increment. **The whole app compiles clean** (`dotnet build` on macOS). Shipped:

**Verified against the live API (test account `messenger`):**
- The account is a **subscriber** (`customerStatus: "subscriber"`) → subscriber-gated
  features (media upload, creates) are exercisable.
- **Two CLAUDE.md "constraints" were outdated** and are now corrected: `GET
  /api/organizations/{id}/members`, `/api/linkedin/targets`, and
  `/api/linkedin/posting-targets` return **200** with the bearer token (were
  documented as 401 walls) — Organizations member-management and LinkedIn
  per-page targeting are now buildable. And the sync-token **does** have a
  server-side revoke: `GET /api/user/sessions` + `DELETE /api/user/sessions/{id}`
  (the test account has **502** accumulated stale tokens — a concrete reason to
  surface this).
- Genuinely cookie-session-only (401 w/ bearer): `/api/user/engagement`,
  `/api/user/dashboard-layout`, Stripe billing. Those stay browser-handoff / out
  of scope.

**Service layer (all compiling, shapes live-verified):** Phase-0 shared HTTP
helpers; new partials for Messages-depth, People, Follow, Direct Messages,
Moderation, Account/Sessions, Exports; Lists row edit/delete + metadata; compose
now supports reply (`parentId`), scheduling (`scheduledAt`), and LinkedIn cross-post.

**UI shipped:**
- ✅ **Feed depth** — reply, view replies (thread), edit & delete own posts,
  report others' posts, inline on each card.
- ✅ **Direct Messages** — new nav item; recipient list + conversation thread +
  composer (send is read-after-write; POST body shape flagged for verification).
- ✅ **People** — new nav item; profile lookup, follow/unfollow, follow-request
  approve/reject, a user's messages, follower/following counts.
- ✅ **Settings** — new nav item; profile edit, **API-session list + revoke**,
  notification-preference toggles, blocked/muted lists with unblock/unmute.

**Built but not yet surfaced in UI (service methods exist):** CSV exports
(4 endpoints), list row edit/delete. **Still open:** see remaining unchecked
boxes below (media-upload UI, scheduled-post UI, list folders/watchers/sharing,
doc folders/sharing/collab, org member mgmt UI [now unblocked], Materialize,
GitHub, billing handoff, register/forgot-password), plus wiring feed/search
cards to open a profile, and a DM-unread badge on the nav.

---

## Progress — Session 2 (2026-07-31)

Continued toward parity. New live-verified shapes (org members `{members:[{id,
username,displayName,avatar,role,active,joinedAt}]}`, member add `{userId,role}`,
folders `{name,parentId}`). **Materialize (`{source}`, opaque) and media upload
(multipart, undocumented response) were deferred** — they need a live write to
pin down, unlike everything below.

**Services added (compiling):** org member management (list/add/change-role/remove)
+ org update/delete; document folder CRUD + create-doc-in-folder.

**UI shipped this session:**
- ✅ **CSV data export** (Settings) — messages / lists / list-rows / follows, each
  via a native Save-file dialog.
- ✅ **List rows: edit + delete** (Lists) — inline JSON editor + per-row delete;
  rows are no longer write-once.
- ✅ **Block / mute / report a user from their profile** (People).
- ✅ **Organizations: member management** — list members, add (via user search),
  change role, remove, edit/delete org (owner/admin gated). *[sub-agent]*
- ✅ **Documents: folder management** — create/rename/delete folders, new doc in
  folder. *[sub-agent]*

**Still deferred / open:** media-upload + scheduled-post compose UI, Materialize
("Create from…"), list folders/watchers/sharing, doc sharing/collaborators,
GitHub (needs the account to link GitHub first), billing handoff, register/
forgot-password, profile-navigation from feed cards, DM inbox-folder + polling +
image attach.

---

## Progress — Session 3 (2026-07-31) — ship-readiness batch

Verified the last write shapes live (image upload: multipart field **`file`** →
`{url}`; avatar `{url}`; email `{newEmail}`; delete `{username,email}`), then
built the remaining ship-critical features. **App builds clean in Debug AND
Release, and the self-contained `win-x64` publish succeeds** (exactly what the
WiX MSI job harvests).

**Shipped this session:**
- ✅ **Image attachments on posts** — upload from disk in compose (multipart),
  pending-thumbnail strip with remove, and images render in feed cards.
- ✅ **Profile navigation** — author names in the feed are clickable → open that
  user in the People tab (via a new `Navigator` hub).
- ✅ **Granular notifications** — mark-one-read + delete-one in the Alerts rail.
- ✅ **Account settings** — avatar-from-URL + email-change request in Settings.
- Services: multipart helper, message image upload, avatar/email/delete-account,
  notification mark-one/delete. (`DeleteAccountAsync` exists but is intentionally
  **not** surfaced — destructive + untestable.)

Feature set is now broad enough to ship as a capable native client. Remaining
items (scheduled-post UI, video upload, list/doc sharing, Materialize, GitHub,
billing, register/forgot-password) are tracked below as post-v1.

---

## Progress — Session 4 (2026-08-01) — media, scheduling, DM depth, sharing

Verified last shapes live (video upload multipart field **`file`** → `{url}`;
DM image upload same; I **can read a shared list's rows** with the bearer token).
App builds clean in Debug + Release.

**Shipped this session:**
- ✅ **Scheduled posts** — compose date/time picker + a header toggle showing
  your scheduled posts (`GET /api/messages/scheduled`, post with `scheduledAt`).
- ✅ **Video upload** on posts — attach from disk, chips in compose, "🎬 Play
  video" link in cards (opens in browser).
- ✅ **Direct Messages depth** *(sub-agent)* — image attachments (upload +
  display), trash/restore own messages, and **5-second live polling** of the
  open thread (`.../updates`, deduped by id).
- ✅ **Lists "Shared with me"** *(sub-agent)* — lists others shared with you
  (`GET /api/lists/watching`), read-only row viewing.
- Services: video upload, scheduled fetch, DM image/restore/updates, `videoUrls`
  on compose, `GetWatchingListsAsync`.

**Remaining post-v1:** list watcher *management* + share-link creation (empty
data on the test account — needs a second account to verify), document sharing/
collaborators, Materialize, GitHub (needs GitHub linked), billing handoff,
register/forgot-password, account-deletion UI, DM inbox-folder view.

---

## 1. Parity snapshot by domain

| Domain (product's name) | Web/API has | App has today | Status |
|---|---|---|---|
| **Messages / Feed** | feed, post, dig, search, **replies/threads, edit, delete, report, link-unfurl, scheduled, image+video upload, per-user timeline** | feed, post, dig/undig, search | 🟡 Partial |
| **Direct Messages** | full 1:1 DM: inbox, threads, send, read/unread, trash/restore, image attach | — nothing | 🔴 Missing |
| **People / Profiles** | public profile, user lookup, a user's messages/lists/documents | — (search returns users but no profile view) | 🔴 Missing |
| **Social graph (Follow)** | follow/unfollow, requests, approve/reject, remove follower, status, followers/following/mutual, counts | counts only | 🔴 Missing (read-only stub) |
| **Blocking / Muting / Reporting** | block, mute, report user, report message, list blocks/mutes | — nothing | 🔴 Missing |
| **Lists** | browse, create, delete, rows, **edit/delete row, single-list metadata, update, schema DSL, folders, watchers/sharing, share-links, connections, contributors, watching, GitHub refresh** | browse, create, delete, view rows, add row, search | 🟡 Partial |
| **Documents** | CRUD, templates, search, **folder CRUD, tree, delta-sync, images, share-links, collaborators, presence/live cursors, seed-defaults** | root list, folders (read), templates (read), CRUD, from-template, search | 🟡 Partial |
| **Organizations** | browse, create, get, **update, delete, members mgmt, org users, LinkedIn org integration** | browse (public + mine), get, create | 🟡 Partial (some member endpoints are cookie-auth-only) |
| **Notifications** | tray, mark-all-read, **mark-one, delete-one, preferences** | tray, mark-all-read | 🟡 Partial |
| **Account & Security / Settings** | profile edit, avatar, change-email, delete account, **sessions/token revoke**, notification prefs, dashboard/front-wall layout, engagement, identities verify | — (identities list/remove only) | 🔴 Missing |
| **Auth flows** | login (sync-token), register, forgot/reset password, verify-email, logout, multi-account switch | sync-token login only | 🟡 Partial |
| **Cross-Platform Syndication** | link/unlink identities, compose-time cross-post toggles (Mastodon/Bluesky/LinkedIn/Twitter) | identities list/remove, browser OAuth handoff, compose toggles | 🟢 Mostly done (LinkedIn per-page targeting blocked by auth model) |
| **Create from… (Materialize)** | turn message(s) into a List/Document | — nothing | 🔴 Missing |
| **Exporting Data** | CSV export: messages, lists, list-rows, follows | — nothing | 🔴 Missing |
| **GitHub integration** | repos, issues (list/create/update/comment), assignees, labels | — nothing | 🔴 Missing |
| **Billing / Subscription** | Stripe checkout + portal; subscriber-gated features | — nothing (can't see plan or upgrade) | 🔴 Missing |
| **Dashboard / Widgets** | dashboard & front-wall layout, weather/location, markets/news/bike-share widgets | — nothing | 🔴 Missing (low priority for desktop) |
| **Search** | messages, people, lists, documents | all four | 🟢 Done |
| **Admin** | blog + user administration | — nothing | ⚪ Out of scope (admin-only) |

Legend: 🟢 done · 🟡 partial · 🔴 missing · ⚪ intentionally out of scope

---

## 2. The gap list (prioritized)

Each item cites the real endpoint(s). Tiers reflect user impact for a desktop
client, not raw endpoint count.

### P0 — Core social pillars that make the app feel incomplete without them

- [x] **Replies / threads** — ✅ read + post replies inline on each feed card
      (`GET /api/messages/{id}/replies`, reply via `POST /api/messages` w/ parentId).
- [x] **Edit / delete your own messages** — ✅ inline on own cards
      (`PATCH`/`DELETE /api/messages/{id}`).
- [x] **Follow / unfollow + requests** — ✅ People view: follow/unfollow,
      request approve/reject, status, followers/following. (`.../mutual` service
      exists, not yet surfaced.)
- [x] **People / profile view** — ✅ People view (lookup + profile + their
      messages + counts). *Remaining:* open a profile directly from a feed/search
      card (nav plumbing), and their public lists/documents tabs.
- [x] **Direct Messages** — ✅ MVP shipped (recipient list + thread + send + mark
      read). *Remaining:* the `/api/dm` inbox-folder view, `.../updates` polling,
      trash/restore UI, image attachments; verify the `POST /api/dm` body shape live.

### P1 — Expected table-stakes for a "real" client

- [x] **Moderation: block / mute / report** — ✅ report a message (feed),
      ✅ blocked/muted lists (Settings), ✅ block/mute/report a user from their
      profile (People). All shipped.
- [~] **Account & Settings** — ✅ profile edit (`PATCH /api/user/update`) and
      notification preferences (`GET`/`PATCH`). *Remaining:* avatar upload
      (`POST /api/user/avatar/*`, needs multipart), email change, delete account.
- [x] **Session / token management** — ✅ **VERIFIED REAL & SHIPPED.** Settings
      lists active sync-tokens and revokes them (`GET /api/user/sessions`,
      `DELETE /api/user/sessions/{id}`). CLAUDE.md corrected — the "no revoke
      endpoint" claim was stale. (Test account had 502 stale tokens.)
- [x] **List rows: edit + delete** — ✅ shipped: inline JSON editor + per-row
      delete in ListsView (`UpdateListRowAsync`/`DeleteListRowAsync`).
- [x] **List metadata & update** — service done (`GetListAsync`/`UpdateListAsync`).
- [ ] **Media attachments on posts** — `POST /api/messages/images/upload`,
      `POST /api/messages/videos/upload` (subscriber-gated; multipart — deferred).
- [ ] **Notifications: granular** — `PATCH /api/notifications/{id}/read`,
      `DELETE /api/notifications/{id}`.
- [x] **Data export** — ✅ shipped: Settings → messages/lists/list-rows/follows
      as CSV via Save-file dialog (`GET /api/exports/*`).

### P2 — Depth features that unlock collaboration & organization

- [ ] **Scheduled messages** — `GET /api/messages/scheduled` + scheduled-post
      option on `POST /api/messages`.
- [ ] **List folders** — `GET`/`POST`/`PUT`/`DELETE /api/folders`.
- [ ] **List watchers / sharing** — `GET`/`POST /api/lists/{id}/watchers`,
      `.../me`, `.../users`, `PUT`/`DELETE .../watchers/{userId}`;
      share-links `GET`/`POST /api/lists/{id}/share-links`, `DELETE .../{token}`;
      resolve `GET`/`POST /api/lists/shared/{token}`, `.../data`;
      `GET /api/lists/watching`, `GET /api/lists/{id}/contributors`.
- [ ] **List connections** — `GET`/`POST /api/lists/connections`,
      `DELETE /api/lists/connections/{id}`.
- [~] **Document folders (full CRUD) + tree** — ✅ create/rename/delete folder +
      new-doc-in-folder shipped in DocumentsView. *Remaining:* `GET /api/documents/tree`,
      move/reparent.
- [ ] **Document sharing & collaboration** — share-links
      `GET`/`POST`/`DELETE /api/documents/{id}/share-links`;
      collaborators `GET`/`POST /api/documents/{id}/collaborators`, `.../users`,
      `PUT`/`DELETE .../{userId}`; images `POST /api/documents/{id}/images/upload`.
- [ ] **"Create from…" (Materialize)** — `POST /api/materialize` (message → List/Document).
- [ ] **Message link unfurl** — `POST /api/messages/{id}/metadata`.

### P3 — Nice-to-have / platform-gated / lower desktop value

- [x] **Organizations: manage** — ✅ shipped: member list/add/change-role/remove
      + edit/delete org (owner/admin gated) in OrganizationsView.
      **Correction:** `.../members` is **not** cookie-session-only — it returns
      200 with the bearer token (re-verified 2026-07-31); the old 401 note was stale.
- [ ] **GitHub integration** — `GET /api/github/repos|issues`, `POST /api/github/issues`,
      `PATCH /api/github/issues/{owner}/{repo}/{number}`, comments, assignees, labels.
- [ ] **Billing / subscription** — `POST /api/stripe/create-checkout-session`,
      `create-portal-session` (both cookie-session-only → likely browser handoff).
- [ ] **Auth self-service** — register / forgot-password / reset-password /
      verify-email / logout / multi-account switch (`/api/auth/*`).
- [ ] **Document delta-sync** — `GET`/`POST /api/documents/sync` (offline/merge; big lift).
- [ ] **Live presence / cursors in documents** — `POST`/`DELETE /api/documents/{id}/presence`.
- [ ] **Dashboard / front-wall layout + widgets** — `GET`/`PUT /api/user/dashboard-layout`,
      `.../front-wall-layout`, `/api/widgets/*`, `/api/weather`, `/api/location`,
      `GET /api/user/engagement`.

### ⚪ Deliberately excluded (not client features)
Cron (`/api/cron/*`), webhooks (`/api/webhooks/*`), admin (`/api/admin/*`),
`test-db`, `analytics/ingest`, `architecture-aggregates`, `oauth/client-metadata`,
`images/proxy`, `openapi.json`, push device registration (`/api/push/*` — mobile).

---

## 3. Implementation plan

The plan closes gaps in **priority order**, front-loading a small amount of
reusable plumbing so each subsequent domain is cheap. Every phase follows the
codebase's existing conventions: one partial-class file per domain in
`Services/`, a `ViewModel` per view (`CommunityToolkit.Mvvm`), a self-contained
`UserControl` in `Views/` that news up its own VM from `AppServices.Session`,
and Strata design tokens only. **Read-after-write** stays the default for any
write whose response envelope isn't live-verified (§5).

### Phase 0 — Shared plumbing (do first, ~small)

Nothing user-visible; makes everything after it faster and consistent.

1. **Generic paginated GET helper + typed error surfacing.** The five domain
   partials each hand-roll `SendAsync → EnsureSuccessAsync → ReadFromJsonAsync`.
   Extract `GetJsonAsync<T>(path)` / `PostJsonAsync<T>(path, body)` helpers on
   `InterlinedApiClient` so new endpoints are one line.
2. **Multipart upload helper** for the four image/video upload endpoints
   (`SendMultipartAsync(path, stream, fileName, contentType)`). Needed by DM,
   messages, documents, avatar.
3. **Profile navigation contract.** Add a lightweight `NavigateToProfile(username)`
   hook on the shell (`MainWindow`) so feed cards, search results, DM threads,
   and follower lists can all open a `ProfileView` (built in Phase 2).
4. **New models** land in `Models/` per domain as each phase needs them (wire
   types matching real JSON — keep the "don't strictly type unverified write
   envelopes" rule).

### Phase 1 — Complete the Feed (P0 messages depth)

*Endpoints:* `GET /api/messages/{id}`, `GET /api/messages/{id}/replies`,
`POST /api/messages/{id}/reply-counts`, `PATCH`/`DELETE /api/messages/{id}`,
`POST /api/messages/{id}/report`, `POST /api/messages/{id}/metadata`,
`POST /api/messages/images/upload` / `videos/upload`, `GET /api/messages/scheduled`.

- **Services:** extend `InterlinedApiClient.cs` (or a new `.Messages.cs` partial)
  with `GetMessageAsync`, `GetRepliesAsync`, `PostReplyAsync` (POST with parent
  id), `EditMessageAsync`, `DeleteMessageAsync`, `ReportMessageAsync`,
  `FetchLinkMetadataAsync`, `UploadMessageImageAsync`, `GetScheduledAsync`.
- **ViewModels/Views:** add a **thread/detail view** (message + reply list +
  inline reply composer) reachable by clicking a feed card; add edit/delete/report
  affordances to `MessageItemViewModel`; add an image-attach picker + scheduled-post
  toggle to the composer in `FeedViewModel`.
- **Design:** replies indent under the parent; the amber "Dug" left-edge rule
  already exists — reuse it. Report uses a small confirm dialog (no browser modal
  dialogs).
- **Risks:** reply payload shape and `reply-counts` body must be live-probed;
  media upload is subscriber-gated → handle `402/403` gracefully.

### Phase 2 — People & the social graph (P0)

*Endpoints:* `GET /api/users/{username}`, `GET /api/user/{username}/messages`,
`GET /api/users/{username}/lists|documents`, `GET /api/users/lookup`;
all of `/api/follow/*`.

- **Services:** new `InterlinedApiClient.People.cs` (profile fetch + a user's
  public content) and `InterlinedApiClient.Follow.cs` (follow/unfollow, requests,
  approve/reject, remove, status, followers/following/mutual — counts already
  exist, move it here).
- **ViewModels/Views:** new **`ProfileView`** (`ProfileViewModel`) showing
  avatar/bio/counts, a Follow/Unfollow button reflecting `.../status`, tabs for
  the user's messages/lists/documents; a **Follow Requests** panel (approve/reject)
  surfaced in the right rail or Alerts area. Wire `NavigateToProfile` from Phase 0
  into feed cards and search results.
- **Payoff:** turns the existing user-search stub into a real social experience;
  unblocks "who follows me" and request management.

### Phase 3 — Direct Messages (P0, whole new nav item)

*Endpoints:* all of `/api/dm/*`.

- **Services:** new `InterlinedApiClient.DirectMessages.cs`.
- **ViewModels/Views:** new **`MessagesView`** (rename-safe: call it *Direct
  Messages* to avoid clashing with Feed) — left column = conversation list
  (`GET /api/dm`, unread badges from `GET /api/dm/unread-count`), right column =
  thread (`GET /api/dm/thread/{username}` + composer + image attach). Poll
  `.../updates` on a timer for near-real-time; mark read on open
  (`POST /api/dm/{id}/read`); trash/restore in a context menu.
- **Shell:** add a **DM** entry to the left nav in `MainWindow.xaml.cs`
  `NavItem_Click` (per-tag `_views` cache pattern already there) and a global
  unread badge on that nav item.
- **Risks:** polling cadence vs. rate limits (`GET /api/limits`); this is the
  largest single new surface — budget accordingly.

### Phase 4 — Moderation + Account & Settings (P1)

*Endpoints:* `/api/users/{username}/block|mute|report`, `GET /api/user/blocks|mutes`,
`POST /api/messages/{id}/report` (from Phase 1); `PATCH /api/user/update`,
`POST /api/user/avatar/*`, `POST /api/user/change-email/request`,
`GET`/`PATCH /api/user/notification-preferences`, `POST /api/user/delete`,
`GET /api/user/sessions`, `DELETE /api/user/sessions/{id}`.

- **Services:** `InterlinedApiClient.Moderation.cs` + `InterlinedApiClient.Account.cs`.
- **ViewModels/Views:** a new **Settings** view with tabs — *Profile* (edit +
  avatar upload), *Notifications* (prefs), *Blocked & Muted* (lists with unblock/
  unmute), *Sessions* (list active tokens, revoke — **pending §5 verification**),
  *Account* (change email, delete). Add block/mute/report to profile and message
  context menus (Phase 1/2 hooks).
- **Security note:** if `DELETE /api/user/sessions/{id}` is real, update
  `CLAUDE.md` and the `CredentialStore` docs — the standing-credential caveat
  would no longer be strictly true.

### Phase 5 — Lists & Documents depth (P1→P2)

*Lists:* row edit/delete (`GET`/`PUT`/`DELETE /api/lists/{id}/data/{rowId}`),
metadata/update (`GET`/`PUT /api/lists/{id}`), folders (`/api/folders`), watchers
& share-links, connections, contributors, `GET /api/lists/watching`,
`POST /api/lists/{id}/refresh`.
*Documents:* folder CRUD + `GET /api/documents/tree`, share-links, collaborators,
image upload, `seed-defaults`.

- **Services:** extend `.Lists.cs` and `.Documents.cs` partials.
- **ViewModels/Views:** add inline **row editing/deletion** to `ListsView`
  (biggest immediate value — today rows are write-once); a folder tree for both
  Lists and Documents; a **Share** dialog (create/revoke links, invite
  watchers/collaborators by user search) shared between the two domains.
- **Schema DSL:** still deferred — `PUT /api/lists/{id}/schema` is only partially
  reverse-engineered (`CLAUDE.md`); keep freeform rows as the supported path
  until the DSL is verified against a test account.

### Phase 6 — Cross-cutting extras (P2→P3)

- **Exports:** `GET /api/exports/*` → a simple "Export to CSV" menu (save-file
  dialog) per domain. Cheap, high perceived value.
- **Create from… / Materialize:** `POST /api/materialize` → a "Turn into
  List/Document" action on feed cards and multi-select.
- **Organizations manage:** `PUT`/`DELETE /api/organizations/{id}`, org users.
  **Skip members mgmt** — cookie-session-only (401 with bearer). Note the block
  in-UI rather than shipping a failing button.
- **GitHub integration:** repos/issues panel (P3) — only if users ask; sizable
  and orthogonal to the social core.
- **Billing:** surface plan status + "Manage subscription" that opens the Stripe
  portal **in the OS browser** (same handoff pattern as OAuth), since checkout/
  portal creation is cookie-session-only. This at least makes subscriber-gated
  features (media upload, some creates) explainable in-app.
- **Auth self-service:** register / forgot-password as pre-login screens; logout;
  multi-account switch. Lower priority because sync-token login already works.

### Phase 7 — Deferred / research-grade (P3)

Document delta-sync (`/api/documents/sync`), live presence/cursors, dashboard &
front-wall layouts, widgets/weather. These are either large (offline-merge),
low desktop value, or web-dashboard-specific. Track but don't schedule until the
core social parity above is closed.

---

## 4. Suggested sequencing & milestones

| Milestone | Phases | Outcome |
|---|---|---|
| **M1 — "A real feed"** | 0, 1 | Threads, edit/delete, report, media, scheduled posts |
| **M2 — "Social"** | 2, 3 | Profiles, follow graph, Direct Messages |
| **M3 — "Trust & self-service"** | 4 | Moderation + full Settings/Account |
| **M4 — "Power features"** | 5 | Editable list rows, folders, sharing/collab |
| **M5 — "Everything else"** | 6, (7) | Exports, materialize, GitHub, billing handoff |

M1–M2 close the visible parity gap for a *social* product; M3 makes it
trustworthy; M4–M5 reach functional parity minus the auth-model-blocked and
web-dashboard-specific corners.

---

## 5. Cross-cutting engineering notes & verification checklist

These apply to **every** phase and encode the constraints already learned in
this codebase (`CLAUDE.md` "load-bearing constraints"):

1. **Live-verify before typing write responses.** Keep the read-after-write
   pattern for any mutating endpoint whose envelope hasn't been confirmed against
   a real (test) account. Don't strictly deserialize an unverified body.
2. **Re-probe the auth model per endpoint.** Bearer sync-token works for most,
   but **not all** — org members (`/api/organizations/{id}/members*`) and
   LinkedIn targets return 401 with bearer. Before building any `session`-scoped
   endpoint (billing, some auth/account flows), confirm it accepts the bearer
   token; if not, it's either browser-handoff (like OAuth) or genuinely blocked.
3. **⚠️ Resolve the sessions/revoke contradiction (do this early).**
   `CLAUDE.md` says the sync-token has no server-side revoke; the spec advertises
   `GET /api/user/sessions` + `DELETE /api/user/sessions/{id}`. Probe both with
   the `.env` test account. Whichever is true, **update `CLAUDE.md` and the
   memory note** so the standing-credential guidance is correct.
4. **Subscriber gating is real.** Many creates + media uploads require a
   subscriber (`POST /api/lists`, `/api/folders`, doc creates, image/video
   upload). Handle `402/403` as a first-class "upgrade needed" state, not a
   generic error — this is why Phase 6 billing (plan visibility) matters.
5. **Media = multipart, not JSON.** The current client only sends JSON. Image/
   video/avatar uploads need the Phase 0 multipart helper.
6. **Pagination everywhere.** Follow lists, DM threads, replies, blocks/mutes,
   a user's messages all paginate — reuse the existing `Pagination`/`limit+offset`
   convention and infinite-scroll pattern from `FeedViewModel`.
7. **OAuth / browser handoffs stay external.** No WebView2 in this app — OAuth
   linking (existing) and any Stripe portal/checkout (Phase 6) open the OS
   default browser and rely on the user returning and refreshing. Don't attempt
   inline web auth.
8. **Design tokens only.** New views (Profile, DM, Settings, Share dialogs) must
   use Strata tokens — teal structure, green actions, amber for live/Dig — sharp
   3–4px corners, the 4pt grid. No new colors/fonts/radii.
9. **Avoid modal browser dialogs / native message boxes that block** the WPF
   dispatcher during long polls (DM updates) — prefer non-blocking toasts and
   confirm-in-place UI.
