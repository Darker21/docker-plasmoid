# Plasma Docker Monitor

[![API CI](https://github.com/Darker21/docker-plasmoid/actions/workflows/api-ci.yml/badge.svg?branch=develop)](https://github.com/Darker21/docker-plasmoid/actions/workflows/api-ci.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/Darker21/docker-plasmoid/badges/coverage.json)](https://github.com/Darker21/docker-plasmoid/actions/workflows/api-ci.yml)
[![Plasmoid CI](https://github.com/Darker21/docker-plasmoid/actions/workflows/plasmoid-ci.yml/badge.svg?branch=develop)](https://github.com/Darker21/docker-plasmoid/actions/workflows/plasmoid-ci.yml)

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

## Installation

You need two pieces: the API, running as a container next to Docker, and the
widget on a **KDE Plasma 6** desktop. Both come from
[Releases](https://github.com/Darker21/docker-plasmoid/releases). The examples
below use `1.0.0`; substitute the latest version.

### 1. Run the API

The image is published to `ghcr.io/darker21/docker-plasmoid-api`. Save this
as `docker-compose.yml`:

```yaml
services:
  api:
    image: ghcr.io/darker21/docker-plasmoid-api:1.0.0
    restart: unless-stopped
    ports:
      - "127.0.0.1:5169:8080"
    group_add:
      - "${DOCKER_GID:?set DOCKER_GID to the gid owning /var/run/docker.sock}"
    security_opt:
      - label=disable
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - logs:/app/Logs

volumes:
  logs:
```

then start it and check it can see your containers:

```bash
DOCKER_GID=$(stat -c '%g' /var/run/docker.sock) docker compose up -d
curl http://localhost:5169/containers
```

The settings are all needed. See [Running the API in Docker](#running-the-api-in-docker)
for why each one is there. Most importantly, keep the port bound to `127.0.0.1`:
the API has no authentication, and access to the Docker socket is
root-equivalent on the host.

To pin the exact image you verified, replace the tag with the digest from the
release notes (`ghcr.io/darker21/docker-plasmoid-api@sha256:…`). If you'd
rather not use a registry, each release also includes the image as a tarball
for `docker load -i docker-plasmoid-api-<version>.image.tar.gz`.

### 2. Install the widget

Download the `.plasmoid` and verify it against the release's checksums:

```bash
VERSION=1.0.0
BASE=https://github.com/Darker21/docker-plasmoid/releases/download/v$VERSION
curl -LO "$BASE/plasma-docker-monitor-$VERSION.plasmoid" -LO "$BASE/SHA256SUMS"
sha256sum -c --ignore-missing SHA256SUMS
```

Install it:

```bash
kpackagetool6 -t Plasma/Applet -i plasma-docker-monitor-$VERSION.plasmoid
```

or graphically: right-click the desktop or panel → **Add Widgets…** → **Get
New Widgets** → **Install Widget From Local File…**. Then add **Docker
Plasmoid** from the widget list to your panel or desktop.

It connects to `http://localhost:5169` by default, which matches the compose
file above. If the API is elsewhere, change it under right-click → **Configure…**.

To upgrade, use `-u` instead of `-i` with the newer file. To uninstall, run
`kpackagetool6 -t Plasma/Applet -r com.github.darker21.docker-plasmoid`.

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

### Running the API in Docker

```bash
DOCKER_GID=$(stat -c '%g' /var/run/docker.sock) docker compose up -d --build
```

(or put `DOCKER_GID=<gid>` in a `.env` file next to `docker-compose.yml` —
it's gitignored since the gid is host-specific).

The compose file handles the three things the container needs to work:

- **Socket access:** the image runs as a non-root user, so it's added to the
  host group owning `/var/run/docker.sock` via `DOCKER_GID`.
- **SELinux:** on enforcing hosts (e.g. Fedora) the container is still denied
  the socket without `label=disable`; on other hosts the option is a no-op.
- **Loopback-only port:** published on `127.0.0.1:5169`, matching the
  plasmoid's default API URL. The API has no auth yet and the socket mount
  is root-equivalent on the host, so don't expose it beyond localhost.

The container runs in `Production`, so `/scalar` isn't available there. Logs
go to the console (`docker compose logs -f`) and to the `logs` named volume
mounted at `/app/Logs`.

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
`/openapi/dev.yaml` directly) rather than duplicated here, so this table
doesn't go stale as the DTOs evolve. The document is named after the build's
release version (`AppVersion`), so local builds serve `dev` and a release
build of `1.2.3` serves `/openapi/1.2.3.yaml`.

### Running the tests

```bash
dotnet test
```

The suite covers `LocalDockerService` (mocked `IDockerClient`), the endpoints
(integration tests via `WebApplicationFactory`, with `IDockerService` mocked),
and `Program.cs`'s own composition root. CI (`api-ci.yml`) fails the build if
line or branch coverage drops below `MIN_COVERAGE`, and the coverage badge
above is published from `develop` on every run. Source-generated code under
`obj/` is excluded via `coverage.runsettings`, which the test project picks up
automatically. To check coverage yourself:

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

## Releasing

Releases are cut by `.github/workflows/release.yml`, either by pushing a tag:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

or from the Actions tab (**Release → Run workflow**, enter `1.0.0`), which
tags the selected branch itself. The tag is the version source of truth: it's
stamped into the plasmoid's `metadata.json`, and into the API assembly and
OpenAPI document via `-p:ReleaseVersion` (the image's `VERSION` build arg).
Versions with a suffix (`1.1.0-rc.1`) become prereleases.

The workflow runs the tests, then creates a **draft** release containing:

- `plasma-docker-monitor-<ver>.plasmoid` — install with `kpackagetool6 -i`
- `plasma-docker-monitor-api-<ver>.openapi.json` — the generated API document
- `docker-plasmoid-api-<ver>.image.tar.gz` — the image, for `docker load`
- `SHA256SUMS` for all of the above

The image is also pushed to `ghcr.io/darker21/docker-plasmoid-api` under its
version tag, and the notes list its registry digest alongside the file
checksums, plus one line per commit since the previous tag. `:latest` is only
moved when you publish the draft (`promote-latest.yml`, skipped for
prereleases), and points at that same digest.

## Roadmap

Condensed from the spec's §6 — see that section for full detail:

1. **Vertical slice** — container list + start/stop/restart ✅
2. **Configurability** — global display toggles, per-item overrides, manual ordering ✅
3. **Compose stacks** — grouping + aggregate stats, accordion UI ✅
4. **Sort modes + exclusion** — uptime/CPU/mem sorting, scope-path-based exclusion, freeze-on-arrow-click
5. **Remote hosts** — widget-per-host, and/or single-widget multi-host aggregation (auth mechanism still undecided — spec §7)

## License

Licensed under the [GNU General Public License v3.0 (or later)](LICENSE).
