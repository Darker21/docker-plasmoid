# Plasma Docker Monitor

A KDE Plasma widget (plasmoid) for monitoring and controlling Docker containers,
backed by a small custom API rather than talking to the Docker socket directly
from QML. The API exists so that "local Docker today, remote homelab hosts
later" is a backend concern the plasmoid never has to know about.

> **Status:** early development. The backend API and the plasmoid both cover
> container listing/actions, compose-stack grouping, and config (toggles,
> per-item overrides, manual ordering). Sort modes, exclusion, and multi-host
> are still ahead. See [Status](#status) below for exactly what works today.

## Overview

- **v1 scope:** monitor and control containers on a single local Docker host
  from a Plasma widget — see running state, start/stop/restart, view
  CPU/memory stats.
- **Later — multi-host:** the same plasmoid code and config schema will
  support either one widget instance per homelab host, or a single widget
  backed by one API instance that aggregates several hosts server-side. The
  plasmoid itself never needs to know which mode it's in — it just renders
  whatever tree shape the API gives it.
- **Design principle:** the plasmoid stays "dumb." All grouping/normalization
  logic (e.g. detecting docker-compose stacks) lives in the API, not the QML.

```
[Plasmoid: QML/JS]  <-- polls -->  [Backend API: ASP.NET Core]  <--talks to-->  [Docker Engine API]
                                                                  (local socket today,
                                                                   remote later)
```

The full design — endpoint contracts, DTO shapes, the plasmoid's config
schema, sort/ordering behaviour, multi-host aggregation, build phases, CI/CD
and release plans — is written up in
**[docker-plasmoid-spec.md](docker-plasmoid-spec.md)**. That document is the
source of truth for *why* things are shaped the way they are; this README is
just the entry point.

## Status

This is being built in phases (see the spec's §6 for the full breakdown).
**Phases 1-3 are done on both the API and plasmoid side**; Phase 4 (sort
modes, exclusion) and Phase 5 (multi-host) haven't been started.

| Piece | State |
|---|---|
| API: `GET /containers`, grouped into `standalone`/`stacks` by compose project | ✅ Implemented |
| API: `POST /containers/{id}/start\|stop\|restart` | ✅ Implemented |
| API: `GET /containers/{id}/stats` (single snapshot) | ✅ Implemented |
| API: `GET /compose/{stackName}/stats` (aggregated stack stats) | ✅ Implemented |
| API: exclusion list, sort-mode-aware aggregates | ⬜ Not started (Phase 4) |
| API: multi-host aggregation | ⬜ Not started (Phase 5) |
| Plasmoid: container list + start/stop/restart (`main.qml`, `ContainerRow.qml`) | ✅ Implemented |
| Plasmoid: compose-stack accordion (`StackRow.qml`) | ✅ Implemented |
| Plasmoid: config — API URL, global toggles, per-item CPU/Mem overrides, manual ordering (`configGeneral.qml`) | ✅ Implemented |
| Plasmoid: sort modes, freeze-on-arrow-click, exclusion picker | ⬜ Not started (Phase 4) |
| Plasmoid: multi-host (`host:` scope prefix) | ⬜ Not started (Phase 5) |

## Repository layout

```
docker-plasmoid/
  docker-plasmoid-spec.md         -> full design spec (read this for "why")
  LICENSE
  PlasmaDockerMonitor.sln
  src/
    PlasmaDockerMonitor.Api/      -> the ASP.NET Core backend
      Endpoints/                  -> minimal API route definitions
      Services/                   -> IDockerService + the Docker.DotNet-backed implementation
      Models/                     -> DTOs returned by the API, and the Result/Result<T> pattern
      Enums/
      Options/                    -> strongly-typed config (e.g. Docker socket URI)
      Ioc/                        -> DI/logging setup (Serilog, config-driven via appsettings.json)
  test/
    PlasmaDockerMonitor.Api.Tests/ -> xUnit suite (unit + WebApplicationFactory integration tests)
  plasmoid/
    package/
      metadata.json              -> widget id/name/icon, X-Plasma-API-Minimum-Version: 6.0
      contents/
        ui/
          main.qml                -> polling timer, model, ListView (ContainerRow/StackRow delegates)
          ContainerRow.qml        -> one container: status dot, CPU/Mem, start/stop/restart, reorder arrows
          StackRow.qml            -> one compose stack: header + accordion of child ContainerRows
          configGeneral.qml       -> config UI: API URL, global toggles, per-item overrides, ordering
          containerSettings.js    -> pure logic for the containerSettings config entry
          dockerApi.js            -> shared XHR helpers (GET/POST against the API)
        config/
          main.xml                 -> Plasmoid.configuration schema
          config.qml               -> ConfigModel wiring main.xml's categories to their QML pages
```

See the spec's §5 and §8.1 for the originally-planned shape this follows.

## Running the API locally

**Prerequisites:**
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A reachable Docker daemon. Defaults to the local socket
  (`/var/run/docker.sock` on Linux); override via `Docker:Uri` in
  `appsettings.Development.json` if your daemon lives elsewhere.

```bash
cd src/PlasmaDockerMonitor.Api
dotnet run
```

By default this launches a browser straight to **`/scalar`** — an interactive
OpenAPI UI for exploring and trying out every endpoint without needing the
plasmoid or a raw HTTP client.

Logging is config-driven via the `Serilog` section in `appsettings.json` —
by default it writes to the console and to `Logs/` (rolling hourly), with
`appsettings.Development.json` bumping the minimum level to `Verbose` for
local development.

### API overview

| Endpoint | Description |
|---|---|
| `GET /containers` | List containers, split into `standalone` and `stacks` (grouped by compose project) |
| `POST /containers/{id}/start` | Start a container |
| `POST /containers/{id}/stop` | Stop a container |
| `POST /containers/{id}/restart` | Restart a container |
| `GET /containers/{id}/stats` | Single-snapshot CPU/memory stats for one container |
| `GET /compose/{stackName}/stats` | Aggregated CPU/memory stats across a compose stack's containers |

Full request/response shapes are in the generated OpenAPI doc (`/scalar`, or
`/openapi/v1.json` directly) rather than duplicated here, so this table
doesn't go stale as the DTOs evolve.

### Running the tests

```bash
dotnet test
```

The suite covers `LocalDockerService` (mocked `IDockerClient`), the endpoints
(integration tests via `WebApplicationFactory`, with `IDockerService` mocked),
and `Program.cs`'s own composition root — currently at 100% line/branch
coverage. To check coverage yourself:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Running the plasmoid locally

**Prerequisites:** a KDE Plasma 6 desktop (for `plasmoidviewer`/`kpackagetool6`), and the API running (see above) — the plasmoid talks to it over HTTP, it doesn't touch Docker directly.

Preview it without installing anything, pointed at a package checkout:

```bash
plasmoidviewer -a ./plasmoid/package
```

For a panel-style preview specifically (rather than the floating default):

```bash
plasmoidviewer -a ./plasmoid/package -l topedge -f horizontal
```

To actually install it onto your desktop:

```bash
kpackagetool6 -t Plasma/Applet -i plasmoid/package
```

(`-u` instead of `-i` to update after making changes, once it's already installed.)

Configure the API URL via the widget's own config dialog (right-click → Configure) — it defaults to `http://localhost:5169`, matching the API's default dev profile.

## Roadmap

Condensed from the spec's §6 — see that section for full detail:

1. **Vertical slice** — container list + start/stop/restart ✅
2. **Configurability** — global display toggles, per-item overrides, manual ordering ✅
3. **Compose stacks** — grouping + aggregate stats, accordion UI ✅
4. **Sort modes + exclusion** — uptime/CPU/mem sorting, scope-path-based exclusion, freeze-on-arrow-click
5. **Remote hosts** — widget-per-host, and/or single-widget multi-host aggregation (auth mechanism still undecided — spec §7)

## License

Licensed under the [GNU General Public License v3.0 (or later)](LICENSE).
