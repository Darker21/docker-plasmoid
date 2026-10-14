# Docker Monitoring Plasmoid — Design Spec

**Status:** Design complete, not yet implemented.
**Audience:** Human (Jacob) and AI assistants picking this up later.
**Code policy:** This is a learning exercise. All code in this repo should be written
by Jacob. Any code blocks in this document are pseudocode/scaffolding only — they
describe shape and intent, not literal syntax to paste in.

---

## 1. Overview

A KDE Plasma widget (plasmoid) for monitoring and controlling Docker containers,
backed by a small custom API rather than talking to the Docker socket directly from
QML. The API exists so that "local Docker today, remote homelab hosts later" is a
backend concern the plasmoid never has to know about.

**v1 scope:** local Docker only.

**Later — multi-host:** two deployment modes, both using the exact same plasmoid
code and config schema:

- **Widget-per-host:** multiple plasmoid instances, each pointed at a different
  host's `apiBaseUrl`. From each instance's point of view there is only one host,
  so the hierarchy stays exactly as described in this doc (`scope: "top"` holds
  stacks/standalones directly, nothing changes).
- **Single widget, all hosts:** one plasmoid instance pointed at one API instance
  that itself aggregates other hosts server-side (decided — see §3.5) and returns
  one already-unified tree. The hierarchy becomes Host → Stack → Container, and
  `scope: "top"` holds host nodes instead of stacks/standalones directly. See §3.5
  and §4.8 for details. **The plasmoid itself never branches on which mode it's
  in** — it always renders whatever tree shape it's given.

### Naming
**Project name: Plasma Docker Monitor.** Chosen over an earlier Pokémon-themed
working name (which fit Jacob's internal homelab naming convention but isn't
descriptive or searchable for a public, open-source project on GitHub/KDE
Store). The name is intentionally literal — "Plasma," "Docker," and "Monitor"
are exactly the terms someone searching KDE Store or GitHub for this kind of
tool would use. The backend API component is referred to throughout this doc
simply as "the API" or "the backend instance" rather than a separate codename,
to avoid introducing a second name a reader has to track.

---

## 2. High-level architecture

```
[Plasmoid: QML/JS]  <-- polls -->  [Backend API: ASP.NET Core]  <--talks to-->  [Docker Engine API]
                                                                  (local socket today,
                                                                   remote later)
```

The plasmoid is intentionally "dumb" — it renders what the API gives it, applies
local display preferences (toggles, sort, ordering), and sends action requests back.
All grouping/normalization logic (e.g. compose stack detection) lives in the API, not
the plasmoid.

---

## 3. Backend API

### 3.1 Project structure (pseudocode layout)

```
PlasmaDockerMonitorApi/
  Program.cs                 -> app bootstrap, route registration
  Endpoints/
    ContainerEndpoints.cs    -> GET /containers, POST /containers/{id}/{action}, GET /containers/{id}/stats
  Services/
    IDockerClientWrapper.cs  -> interface so local socket vs remote host is swappable later
    LocalDockerService.cs    -> talks to /var/run/docker.sock via Docker.DotNet
  Models/
    ContainerSummaryDto.cs
    ComposeStackDto.cs
    ContainerActionRequest.cs
  appsettings.json           -> socket path, port, allowed origins
```

Design principle: **never let `Docker.DotNet`'s types leak into API responses.**
Always map onto your own DTOs. This is what makes "remote host later" cheap.

### 3.2 Endpoint contract

```
GET /containers?exclude=path1,path2
  -> returns: { standalone: [ContainerSummaryDto], stacks: [ComposeStackDto] }
  -> exclusion is applied server-side, by scope path with optional wildcard
     segments (see §4.7) — NOT by bare container name, since the same name can
     legitimately exist at multiple positions in the tree (HA setups, dev vs
     deployed instances, etc.)

POST /containers/{id}/start
POST /containers/{id}/stop
POST /containers/{id}/restart
  -> returns 204 on success, error with reason on failure

GET /containers/{id}/stats
  -> single snapshot (not the streaming variant), returns ContainerStatsDto
  -> kept as a separate call from the main list — stats are comparatively
     expensive, the plasmoid should only call this per-container when that
     container's resolved CPU/Mem toggle is on
```

### 3.3 DTOs (pseudocode shape)

```
ContainerSummaryDto {
  id
  name
  state            // running | stopped | restarting | etc.
  image
  startedAt
  composeProject   // null if standalone, else the compose project name
}

ContainerStatsDto {
  cpuPercent
  memUsedMb
  memLimitMb
}

ComposeStackDto {
  projectName
  containers: [ContainerSummaryDto]
  runningCount
  totalCount
  representativeStartedAt   // = earliest startedAt among containers
                            // (i.e. the LONGEST-running container — this is what
                            // "max uptime" means for a stack)
  aggregateCpuPercent       // sum of all child containers' CPU% (only relevant
                            // when sortMode == "cpu" and stats are available)
  aggregateMemUsedMb        // sum of all child containers' mem usage (only
                            // relevant when sortMode == "mem")
}
```

**Why a stack needs `representativeStartedAt` / aggregates:** sort modes (see
§4.4) need one comparable value per *top-level item*, whether that item is a
standalone container or a whole stack. Computing this once in the API means the
plasmoid's sort comparator never needs a special case for "is this a stack."

### 3.4 Compose grouping — ownership decision

**Decided: the API owns grouping (not the plasmoid).** Docker compose containers
carry a `com.docker.compose.project` label; the API reads this and pre-splits the
response into `standalone` and `stacks` rather than returning one flat list and
making the plasmoid re-derive groups from labels. Rationale: keeps QML simpler, and
matches the general direction of "API does the normalization work, plasmoid just
renders."

### 3.5 Multi-host aggregation (decided: server-side, in the API)

**Decided:** when running in "single widget, all hosts" mode, one designated
API instance knows about the other host instances and aggregates their data
server-side, returning one already-unified tree. The plasmoid does **not** call
multiple APIs and merge results itself — it always talks to exactly one
`apiBaseUrl` and gets back one tree, regardless of how many hosts are behind it.

Rationale: consistent with the rest of this design pushing normalization work into
the API and keeping the plasmoid dumb. It also means the plasmoid's QML/config code
needs zero "am I in multi-host mode" branching — the only thing that changes is the
*shape* of what's at the top of the tree (hosts, vs. stacks/standalones directly),
and that's handled generically the same way stacks-vs-standalone already is (§5.2).

This means the aggregating API instance needs:
- Its own config: a list of other host instances to query (address + however auth
  is handled, e.g. local network only, or a shared token — TBD, see §7).
- Fan-out logic: query each configured remote instance's own `/containers`
  endpoint (each remote instance still only knows about itself — no instance needs
  to know it's *being* aggregated), and wrap each remote response in a `HostDto`.
- The local host's own containers are also wrapped in a `HostDto`, so "local vs
  remote" is not a special case at this layer — every host, including the one the
  API itself is running on, is just another `HostDto` in the list.

```
GET /containers?exclude=...      (on the aggregating instance, multi-host mode)
  -> returns: { hosts: [HostDto] }

HostDto {
  hostName                  // e.g. "registeel", "sylveon" — matches homelab naming
  standalone: [ContainerSummaryDto]
  stacks: [ComposeStackDto]
  representativeStartedAt   // earliest startedAt among EVERYTHING under this host
                            // (recursively: longest-running container anywhere
                            // on this host, whether standalone or in a stack)
  aggregateCpuPercent       // sum of all CPU%, everything under this host
  aggregateMemUsedMb        // sum of all mem usage, everything under this host
}
```

This is the same pattern as `ComposeStackDto` (§3.3), just one level further out —
a host aggregates stacks+standalones the same way a stack aggregates containers.

A single-host API instance (today's v1, and every "widget-per-host" deployment
even after multi-host support lands) never produces a `HostDto` wrapper at all —
its `GET /containers` response shape is unchanged from §3.2. Only the designated
aggregating instance, when asked for the all-hosts view, wraps things in `HostDto`.

---

## 4. Plasmoid config schema

All of this lives in `contents/config/main.xml` (`Plasmoid.configuration`). Anything
that needs nested structure is stored as a serialized JSON string, since Plasma's
config entries are flat by nature.

### 4.1 Top-level config entries

```
showCpuStatsGlobal: bool, default false
showMemStatsGlobal: bool, default false
showLogsPreviewGlobal: bool, default false   // global-only, no per-item override (v1)
apiBaseUrl: string, default "http://localhost:5000"
sortMode: enum ["manual", "uptime", "cpu", "mem"], default "manual"
excludedScopes: string (serialized JSON array), default "[]"   // see §4.7 — scope
                                                                 // paths with optional
                                                                 // wildcard segments,
                                                                 // NOT bare names
containerSettings: string (serialized JSON array), default "[]"
```

### 4.2 `containerSettings` shape — scoped, two-level ordering

Containers can live at the top level (standalone, or a stack header) or nested
inside a stack's own scope. `order` is only meaningful *within* its scope, not
globally.

```
containerSettings: [
  { id: "abc123",            scope: "top",        order: 0, cpuOverride: null, memOverride: null },
  { id: "stack:plex-stack",  scope: "top",        order: 1, cpuOverride: null, memOverride: null },
  { id: "def456",            scope: "plex-stack",  order: 0, cpuOverride: true, memOverride: null },
  { id: "ghi789",            scope: "plex-stack",  order: 1, cpuOverride: null, memOverride: null }
]
```

Notes:
- `scope: "top"` covers both standalone containers and stack headers at the root.
- A stack's own synthetic id is prefixed (`stack:<projectName>`) so it can't collide
  with a real container id.
- Containers belonging to a stack get `scope: <projectName>`; their `order` is only
  relative to siblings in that same scope.
- `cpuOverride` / `memOverride`: `null` = inherit global, `true`/`false` = explicit
  per-container choice. Stack headers don't get CPU/Mem overrides themselves (v1) —
  only their child containers do.

### 4.3 Resolving global vs per-item override

```
function resolveShowCpu(containerId):
  override = containerSettings[containerId].cpuOverride
  if override is not null:
    return override
  return showCpuStatsGlobal

// identical pattern for resolveShowMem
```

Every row calls the resolver rather than reading the global flag directly, so an
override transparently wins without the row caring why.

### 4.4 Sort modes

`sortMode` is **global** and applies recursively at **every** scope — top level and
inside every expanded stack, identically. There is no separate inside-stack sort
setting; this was a deliberate simplification over having independent sort
configs per scope.

```
function getOrderedItems(scope, sortMode):
  itemsInScope = containerSettings.filter(s => s.scope == scope)

  if sortMode == "manual":
    return itemsInScope.sortBy(s => s.order)

  return itemsInScope.sortBy(s => getMetricValue(s.id, sortMode))


function getMetricValue(itemId, sortMode):
  item = lookupLiveData(itemId)   // a container or a stack

  if sortMode == "uptime":
    return item.isStack ? item.representativeStartedAt : item.startedAt
  if sortMode == "cpu":
    return item.isStack ? item.aggregateCpuPercent : item.cpuPercent
  if sortMode == "mem":
    return item.isStack ? item.aggregateMemUsedMb : item.memUsedMb
```

### 4.5 Manual reorder while sorted by a live metric (arrows always enabled)

**Decided UX:** the up/down arrows are never disabled. Clicking one while
`sortMode != "manual"` snaps the *currently displayed* order into explicit `order`
values across **all known scopes** (not just the scope clicked in), flips
`sortMode` to `"manual"`, and then performs the swap on top of that frozen
baseline. This matches familiar patterns (e.g. dragging a row in a sorted list
snapping you into custom order) rather than greying out controls that would
otherwise silently do nothing.

```
function moveUp(itemId, scope):
  if sortMode != "manual":
    freezeAllScopesToManualOrder(sortMode)
    sortMode = "manual"

  siblings = getOrderedItems(scope, "manual")
  index = siblings.findIndex(itemId)
  if index > 0:
    swap(siblings[index].order, siblings[index - 1].order)

  save(containerSettings)
  save(sortMode)

// moveDown is symmetric


function freezeAllScopesToManualOrder(currentSortMode):
  for each scope in allKnownScopes:
    currentlyDisplayedOrder = getOrderedItems(scope, currentSortMode)
    for index, item in currentlyDisplayedOrder:
      containerSettings[item.id].order = index
```

**Why "all known scopes," not just the clicked one:** if only the clicked scope is
frozen, then switching to manual mode and later expanding a *different*, untouched
stack would show its containers with stale/default `order` values (likely all `0`,
since they were never explicitly set) instead of the position they were just
visually sitting in under the old sort mode. Freezing every scope at once, exactly
once, at the moment of the first arrow click, avoids unrelated rows jumping around
when mode flips.

This freeze step is flagged as **the trickiest piece of logic in the whole spec** —
everything else here is fairly mechanical, but a subtle bug here would be the most
confusing thing to debug later. Worth writing tests around this specifically.

### 4.6 New item arrival (containers / stacks appearing for the first time)

- A new **standalone container** never seen before → appended to the bottom of
  `scope: "top"` (i.e. gets `order = max(existing top-level orders) + 1`).
- A new **container inside an existing stack** → appended to the bottom of that
  stack's own scope.
- A brand-new **stack** never seen before → its header is appended to the bottom of
  `scope: "top"`, and its containers are assigned sequential `order` values within
  their own new scope as they're first observed (0, 1, 2...).

### 4.7 Exclusion list (by scope path, not bare name)

**Revised from an earlier version of this spec.** Exclusion was originally a flat
list of container names. That breaks the moment the same name can legitimately
exist twice in the tree — e.g. an HA setup with the same container name on two
hosts where only one should be hidden, or a "dev" instance of a service the user
doesn't want cluttering the list alongside its "deployed on a remote host"
counterpart. Both cases need to exclude one specific *position* in the tree, not
every container that happens to share a name.

Exclusion entries are therefore scope paths (see §4.8 for the scope-as-path
concept) reaching all the way down to, and including, the container's own name:

```
excludedScopes: string (serialized JSON array), default "[]"
  // each entry = the full scope path INCLUDING the container's name as the
  // final segment:
  // [
  //   "host:registeel/stack:plex/watchtower",
  //   "host:sylveon/stack:plex/watchtower"        // same name, different host —
  //                                                 // independently excludable
  // ]
```

Path segments are built from **names, not container IDs** — container IDs churn
every time `docker compose up` recreates a container, so an exclusion keyed on ID
would silently stop working the next time that stack gets redeployed. Names (as
defined in `docker-compose.yml`, or the container's given name if standalone) are
the stable identity here.

**Wildcard segments are supported.** A single `*` in place of any one segment
matches any value at that position, so a single rule can apply across hosts (or
across stacks, or even across both):

```
excludedScopes: [
  "*/stack:plex/watchtower",          // exclude "watchtower" in the "plex" stack,
                                       // on ANY host
  "host:registeel/*/dev-redis",       // exclude "dev-redis" in ANY stack,
                                       // but only on the "registeel" host
  "host:registeel/stack:plex/abc"     // exact, no wildcard — only this one
]
```

Matching logic, pseudocode:

```
function shouldExclude(fullScopePath, containerName):
  candidatePath = fullScopePath + "/" + containerName
  candidateSegments = candidatePath.split("/")

  for each rule in excludedScopes:
    ruleSegments = rule.split("/")
    if ruleSegments.length != candidateSegments.length:
      continue   // wildcards match within a segment, not across segment count
    if every (ruleSegments[i] == "*" or ruleSegments[i] == candidateSegments[i]):
      return true

  return false
```

A single-host widget (today's v1; no `host:` segment ever exists) simply has
shorter candidate paths — e.g. `"stack:plex/watchtower"` — and rules without a
`host:` segment match exactly as before. Nothing about single-host exclusion
behavior changes; the path is just shorter because there's one less level to
traverse.

**Where filtering happens — API or aggregating instance, not the plasmoid.**
Consistent with §4.7's original reasoning (no point sending data just to hide
it), filtering stays server-side. A single-host API instance can still
filter before responding, exactly as today, since it only ever sees paths
starting below its own (nonexistent) host segment. The **aggregating** instance
(§3.5) is different: it can't know whether a given container's path crosses a
host boundary until it has assembled the full multi-host tree, so it must apply
`excludedScopes` filtering *after* building the tree, not before. Functionally
identical result to the caller either way — just a different point in each
instance's own pipeline where the check runs.

The exclusion *list itself* (which paths to exclude) remains a plasmoid/config
concern; *applying* it is an API-side concern, same split as before.

---

### 4.8 Scope as a path (generalizing for the Host → Stack → Container case)

Everything in §4.2–§4.6 was written for a two-level hierarchy (top → stack →
container). Multi-host, single-widget mode adds a third level: Host → Stack →
Container. Rather than inventing new fields, `scope` generalizes from "a flat
string" to "a path of arbitrary depth" — the same filtering/sorting functions
(`getOrderedItems`, the freeze logic in §4.5) already treat `scope` as an opaque
string they exact-match against, so they need **no logic changes**, only the
*values* of scope strings get richer.

```
scope: "top"                          // single-host widget — UNCHANGED from §4.2
scope: "host:registeel"               // all-hosts widget — top level lists hosts
scope: "host:registeel/stack:plex"    // inside a host — lists its stacks + standalones
```

A container inside a stack on a specific host has `scope: "host:registeel/stack:plex"`
— structurally identical to today's `scope: "plex-stack"`, just with a host segment
prepended. `containerSettings` entries don't gain new fields:

```
containerSettings: [
  { id: "host:registeel",           scope: "top",                       order: 0, cpuOverride: null, memOverride: null },
  { id: "host:sylveon",             scope: "top",                       order: 1, cpuOverride: null, memOverride: null },
  { id: "stack:plex",               scope: "host:registeel",            order: 0, cpuOverride: null, memOverride: null },
  { id: "abc123",                   scope: "host:registeel/stack:plex", order: 0, cpuOverride: true, memOverride: null }
]
```

**Important — this is purely additive.** A single-host widget (today's v1, and
every widget-per-host deployment) never produces a `host:` row, so `scope: "top"`
is populated directly with stacks/standalones exactly as already specified.
Nothing about the existing two-level behavior changes; three levels only exist
when an aggregating API response (§3.5) actually contains `HostDto` entries.

**Sort modes recurse one level further.** `getMetricValue` (§4.4) gains a host
case using the same pattern as the existing stack case:

```
function getMetricValue(itemId, sortMode):
  item = lookupLiveData(itemId)

  if sortMode == "uptime":
    if item.isHost:  return item.representativeStartedAt
    if item.isStack: return item.representativeStartedAt
    return item.startedAt
  // cpu / mem follow the same isHost -> isStack -> plain-container pattern,
  // reading aggregateCpuPercent / aggregateMemUsedMb at whichever level applies
```

**The freeze-on-arrow-click logic (§4.5) needs `allKnownScopes` to walk three
levels, not assume at most two.** This is an implementation detail of how scopes
get enumerated (walk every host, then every stack within every host), not a
change to the freeze algorithm itself — the algorithm already iterates "every
known scope" generically.

**Config UI** (§5.6) gains one more level of nesting when hosts are present —
host rows behave exactly like stack rows do today (name, running count summed
across the host, up/down arrows, expand to reveal stacks+standalones underneath),
and the nesting is visually one indent deeper than a stack-only widget. No new
QML component is strictly required — `StackRow.qml` may simply end up reused for
both "stack containing containers" and "host containing stacks/standalones," since
the shape (header + running count + expandable children) is identical at both
levels.

---

## 5. Plasmoid (QML) structure

### 5.1 File layout

```
plasmoid/                  -> matches the plasmoid/ folder from §8.1's repo
                               tree; shown here as its own root since this section
                               describes the plasmoid's internal structure in
                               isolation from the rest of the repo
  metadata.json
  contents/
    ui/
      main.qml               -> root: polling timer, model, top-level ListView
      ContainerRow.qml        -> one delegate: status dot, name, toggled fields, action icons
      StackRow.qml             -> one delegate: stack header (expand arrow, name, running count),
                                   accordion-reveals child ContainerRows when expanded
      ConfigGeneral.qml        -> settings page: global toggles, sort mode, exclusion list,
                                   nested per-item override list with up/down arrows
    config/
      main.xml                -> Plasmoid.configuration schema (§4)
```

### 5.2 Top-level list rendering

```
ListView model: mixed list of
  { type: "standalone", data: ContainerSummaryDto }
  { type: "stack",       data: ComposeStackDto }

delegate:
  if item.type == "standalone": ContainerRow.qml { container: item.data }
  if item.type == "stack":       StackRow.qml { stack: item.data }
```

### 5.3 `ContainerRow.qml` (pseudocode)

```
RowLayout:
  StatusDot (color from state: running/stopped/restarting)
  Label (name)
  if resolveShowCpu(container.id): Label (cpu%)
  if resolveShowMem(container.id): Label (mem usage)
  if showLogsPreviewGlobal: Label (last log line, truncated)
  Spacer
  IconButton (start)   -> visible if state != running
  IconButton (stop)    -> visible if state == running
  IconButton (restart)
  IconButton (logs)    -> opens popup/expands row
```

### 5.4 `StackRow.qml` (pseudocode)

```
StackRow.qml:
  property bool expanded: false

  RowLayout (header, always visible):
    ExpandArrow (rotates on click) -> toggles `expanded`
    Label (projectName)
    Label ("{runningCount}/{totalCount} running")
    Spacer
    // no per-container action icons at this level — actions live one level down

  Column (visible: expanded):
    orderedChildren = getOrderedItems(stack.projectName, sortMode)
    Repeater over orderedChildren:
      ContainerRow.qml { container: modelData, indented: true }
```

`indented: true` is purely visual (extra left margin) to show nesting under the
stack header.

### 5.5 Polling loop (`main.qml`, pseudocode)

```
Timer every N seconds:
  -> GET {apiBaseUrl}/containers?exclude={excludedScopes joined}
  -> parse JSON into { standalone, stacks }
  -> build top-level mixed list, ordered via getOrderedItems("top", sortMode)
  -> ListView delegate per item (see §5.2)
```

### 5.6 `ConfigGeneral.qml` (pseudocode)

```
ConfigGeneral.qml:

  Section "General":
    ComboBox "Sort mode" -> bound to sortMode [manual, uptime, cpu, mem]
    TextField/TagList "Excluded scope paths" -> bound to excludedScopes
                                                 // e.g. add via a picker built from
                                                 // the live tree rather than free
                                                 // typing, to avoid typo'd paths
                                                 // that silently never match

  Section "Global Defaults":
    CheckBox "Show CPU stats" -> showCpuStatsGlobal
    CheckBox "Show memory stats" -> showMemStatsGlobal
    CheckBox "Show logs preview" -> showLogsPreviewGlobal

  Section "Containers" (top-level items, ordered by current sortMode):
    ListView:
      if item is standalone:
        Row: name, UpArrow, DownArrow, CPU tri-state [Default/On/Off], Mem tri-state [Default/On/Off]
      if item is stack:
        Row: stack name, UpArrow, DownArrow
        indented ListView over stack's children (ordered by current sortMode):
          Row: name, UpArrow, DownArrow, CPU tri-state, Mem tri-state

  // Arrows are ALWAYS enabled (see §4.5) — clicking while sortMode != "manual"
  // triggers the freeze-then-swap behavior, not a no-op.
```

A tri-state combo (not a checkbox) represents "inherit/on/off" for per-item
overrides, since QML doesn't have a built-in three-state checkbox.

---

## 6. Build phases

**Phase 1 — vertical slice.** API exposes `GET /containers` (flat, no grouping yet)
and `POST /containers/{id}/{action}`. Plasmoid polls and renders a static,
non-configurable list with start/stop/restart buttons. Goal: prove the
socket → API → QML pipeline works at all.

**Phase 2 — configurability.** Add global toggles, per-item overrides, manual
ordering with arrows, config UI. No sort modes or stacks yet — straight manual
order only.

**Phase 3 — compose stacks.** API adds grouping (§3.4), DTOs gain
`representativeStartedAt` / aggregates. Plasmoid adds `StackRow.qml` and accordion
behavior. Config schema gains `scope`.

**Phase 4 — sort modes + exclusion.** Add `sortMode` and the freeze-on-arrow-click
behavior (§4.5), add exclusion list + server-side filtering (§4.7).

**Phase 5 — remote hosts.** Two sub-modes, decided in §1, §3.5, §4.8:
- *Widget-per-host:* trivial — multiple plasmoid instances, each with its own
  `apiBaseUrl`, no code changes needed beyond what Phases 1–4 already built.
- *Single widget, all hosts:* the designated aggregating API instance gains
  config for which other host instances to query, fan-out logic to call each
  remote instance's own `/containers` endpoint, and wraps every host (including
  itself) in a `HostDto`. The plasmoid's `scope` strings gain an optional
  `host:<name>/` prefix segment; `getOrderedItems`/freeze logic need no changes
  (already scope-agnostic), only `allKnownScopes` enumeration needs to walk the
  extra level.

---

## 7. Open / deferred decisions (not yet settled)

- Whether `showLogsPreviewGlobal` ever gets a per-container override, or stays
  global-only permanently.
- Exclusion UX: whether `excludedScopes` entries are added via free-text path
  entry (error-prone — easy to typo a segment and have the rule silently never
  match) or via a picker driven off the live tree (e.g. a right-click/context
  action on an existing row: "exclude this"), which would compose the correct
  path automatically. Leaning toward the picker, not yet built.
- Whether stack headers (and now host headers, §4.8) should ever get their own
  CPU/Mem override toggles (v1 says no — only leaf containers can override).
- How the aggregating API instance authenticates to/discovers other host
  instances (§3.5) — local-network-only trust, shared token, mTLS via the
  existing YubiKey/SSH cert infra, etc. Not yet decided; likely worth reusing
  whatever pattern comes out of the existing homelab SSH certificate work
  (Registeel/Sylveon/Umbreon CA setup) rather than inventing a new auth scheme.
- Whether a remote host instance needs to know it's local-vs-being-aggregated at
  all (current assumption in §3.5: no, every instance only ever returns its own
  single-host shape; aggregation is entirely the aggregating instance's concern).
- **Podman support.** Current `LocalDockerService` talks to the Docker Engine API
  via `Docker.DotNet` against `/var/run/docker.sock` (or a configured `Uri`) —
  it does not support Podman. Podman exposes a Docker-API-compatible socket
  (`podman system service`), so this is likely cheap to add later (possibly just
  a different default `Uri`/socket path, maybe no code changes at all), but it
  hasn't been tried or verified. Flagged as a later target, not v1 scope.

---

## 8. Deployment / publishing (GitHub)

Decisions made: **monorepo** (one repo holds both the API and the plasmoid);
the API is distributed as **both** a Docker image and a systemd-installable
build, chosen per-host at deploy time; the plasmoid is published in **all
three** common forms (manual install from a repo checkout, a packaged
`.plasmoid` zip on GitHub Releases, and KDE Store) so end users — including
future-Jacob on a different machine — can pick whichever fits.

### 8.1 Repo layout

A monorepo means CI needs path-based triggers so a plasmoid-only change
doesn't rebuild/republish the API, and vice versa. Suggested top-level shape:

```
plasma-docker-monitor/        (repo name)
  PlasmaDockerMonitorApi/      -> the ASP.NET Core project (§3)
  plasmoid/                    -> the plasmoid project (§5), folder named
                                   generically since the repo root already
                                   carries the project name
  .github/
    workflows/
      api-ci.yml                 -> build + test PlasmaDockerMonitorApi, triggered on PlasmaDockerMonitorApi/** changes
      api-release.yml            -> on tag matching api-v*, build + push Docker image + publish systemd tarball
      plasmoid-ci.yml             -> lint/package-check plasmoid/, triggered on plasmoid/** changes
      plasmoid-release.yml        -> on tag matching plasmoid-v*, package .plasmoid zip + attach to GitHub Release
  docs/
    docker-plasmoid-spec.md      -> this document, kept in-repo so it stays versioned with the code
  README.md                      -> top-level: what this repo is, links to both subproject READMEs
  PlasmaDockerMonitorApi/README.md  -> API-specific: how to run locally, Docker vs systemd, config
  plasmoid/README.md             -> plasmoid-specific: install instructions for all three methods
```

**Independent tags per subproject** (`api-v1.2.0`, `plasmoid-v1.0.3`) rather than
one shared version number — the API and plasmoid will inevitably drift out of
lockstep (a plasmoid UI tweak doesn't need an API bump, and an API bugfix
shouldn't force a plasmoid republish). The API contract version (§3.2's shape)
is the thing that actually needs tracking for compatibility, not a shared repo
version — consider a `Compatible-API-Version` note in the plasmoid's release
notes once the contract has had a breaking change or two.

### 8.2 Branching / workflow

Given this is a personal learning project rather than a team repo, a
lightweight trunk-based approach fits better than a heavier GitFlow-style setup:

```
main                       -> always deployable; CI runs on every push
feature branches            -> short-lived, PR'd into main (even solo, PRs are a
                               decent forcing function for the "did I actually
                               test this" check, and give you a CI gate before
                               merge)
tags: api-v*, plasmoid-v*    -> these are what trigger release workflows, NOT
                               pushes to main directly
```

Tagging (rather than "every push to main publishes") is what keeps you in
control of when a Docker image actually gets pushed or a GitHub Release
actually gets cut — CI still runs and validates on every push to main, but
*publishing* stays a deliberate, manual `git tag api-v1.0.0 && git push --tags`
action.

### 8.3 API CI (`api-ci.yml`) — pseudocode

```
on: push/PR affecting PlasmaDockerMonitorApi/**

steps:
  - checkout
  - setup .NET SDK
  - dotnet restore (PlasmaDockerMonitorApi)
  - dotnet build (PlasmaDockerMonitorApi)
  - dotnet test (if/when tests exist — flagged in §4.5 as worth having for
    the freeze-on-arrow-click logic specifically, even though that logic
    lives in the plasmoid not the API; API-side tests would cover DTO
    mapping, exclusion-path matching (§4.7), and stack/host aggregation math)
```

### 8.4 API release (`api-release.yml`) — pseudocode

Triggered on `api-v*` tags. Produces **both** distribution forms from the same
build, since the decision was "both, per-host":

```
on: push tag matching "api-v*"

job: build-and-publish-docker
  - checkout
  - setup .NET SDK
  - docker build -> tag image as ghcr.io/<you>/plasma-docker-monitor-api:{tag}, also :latest
  - docker push to GitHub Container Registry (ghcr.io)
    // GHCR is the natural choice here — it's already part of "GitHub as your
    // DevOps app," no separate Docker Hub account/credentials needed, and
    // your existing homelab already pulls from registries this way

job: build-and-publish-systemd-bundle
  - checkout
  - setup .NET SDK
  - dotnet publish -c Release --self-contained -r linux-x64
    // self-contained means the target host doesn't need the .NET runtime
    // pre-installed — fits hosts like Registeel/Sylveon that may not already
    // have it, without forcing a runtime install as a prerequisite
  - tar the published output + an example .service unit file + an install
    script (copies binary to /opt/plasma-docker-monitor-api, installs the unit, prompts
    for the port/socket-path config)
  - attach the tarball to the GitHub Release for this tag
```

A per-host choice then becomes: hosts already running Docker (which, given the
existing homelab, is most of them) pull the GHCR image; a host where you'd
rather avoid another container (or where Docker itself isn't installed) grabs
the systemd tarball from the Release instead. Same versioned artifact set,
two consumption paths.

### 8.5 Plasmoid CI (`plasmoid-ci.yml`) — pseudocode

QML doesn't have "tests" in the traditional sense for most of this project, but
there's real validation worth automating before a release:

```
on: push/PR affecting plasmoid/**

steps:
  - checkout
  - validate metadata.json is well-formed JSON and has required fields
    (id, name, version, api version)
  - validate contents/config/main.xml is well-formed XML
  - (optional, once it exists) run any QML lint tool available for KDE 6
    plasmoids, to catch obvious syntax errors before a human installs it
  - dry-run package the .plasmoid zip (§8.6's packaging step) just to confirm
    it succeeds — without publishing anywhere
```

### 8.6 Plasmoid release (`plasmoid-release.yml`) — pseudocode

Triggered on `plasmoid-v*` tags, produces the packaged zip used by **all
three** install paths described below:

```
on: push tag matching "plasmoid-v*"

steps:
  - checkout
  - zip the plasmoid/ directory contents into plasma-docker-monitor.plasmoid
    // a .plasmoid file IS just a zip with that extension — kpackagetool6
    // and the KDE Store both expect this exact shape
  - attach plasma-docker-monitor.plasmoid to the GitHub Release for this tag
  - (optional, manual step rather than automated — see note below) upload
    the same .plasmoid to KDE Store (store.kde.org) via its web UI
```

**Why KDE Store upload is left manual rather than automated in CI:** KDE
Store's submission flow (via the `kde-store` / Pling ecosystem) typically
expects a logged-in browser session and isn't designed around unattended CI
credentials in the way GHCR or GitHub Releases are. Automating that step is
possible in principle (some projects script it via the Open Collaboration
Services API) but isn't worth the added complexity for a personal project's
v1 — a five-minute manual upload per release is a reasonable trade against
maintaining a second auth flow in CI. Worth revisiting if releases become
frequent enough that the manual step becomes genuinely annoying.

### 8.7 The three plasmoid install paths, from the artifact CI already produces

All three consume exactly one build output (`plasma-docker-monitor.plasmoid`), so
there's no triple-maintenance burden — only the "how does a user get the file
onto their machine and registered with Plasma" step differs:

- **Manual/repo checkout:** clone the repo, `kpackagetool6 -i plasmoid/`
  pointed directly at the source directory (no zip needed at all for this
  path — useful for Jacob's own dev loop, since it's the fastest "make a
  change, see it reflected" cycle without waiting on a release).
- **GitHub Release zip:** download `plasma-docker-monitor.plasmoid` from the
  Release page, `kpackagetool6 -i plasma-docker-monitor.plasmoid`. This is the
  "I don't want to clone a repo, just give me the file" path.
- **KDE Store:** `kpackagetool6` isn't even needed — Plasma's own "Get New
  Widgets" dialog handles install/update directly once it's listed there.
  This is the path for anyone who isn't you and wouldn't think to check
  GitHub at all.

### 8.8 Versioning the API contract itself

Worth deciding early, since the multi-host work (§3.5, §4.8) means the API
contract (§3.2/§3.3 DTOs) will change shape more than once before things
stabilize: include an explicit contract/schema version in API responses (e.g.
a top-level `apiVersion` field, or a versioned route prefix like `/v1/containers`)
so a plasmoid built against an older contract can detect a mismatch with a newer
API and show a clear "update me" message rather than failing silently on an
unexpected JSON shape. Not yet built, but worth designing the DTOs with this in
mind from Phase 1 rather than retrofitting it once a breaking change is already
live on a homelab host.
