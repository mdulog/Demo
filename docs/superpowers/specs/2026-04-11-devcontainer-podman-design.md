# Dev Container + Podman Design

**Date:** 2026-04-11
**Status:** Approved

---

## Goal

Configure a VS Code dev container for the Pacevite .NET 10 API that:
- Works with Podman on Fedora (no Docker daemon required)
- Brings up Postgres automatically alongside the app container
- Reuses the existing `docker-compose.yml` without modification
- Gives contributors an "open and go" experience

---

## Approach

**Option C: Hybrid Compose-based dev container**

Use the devcontainer `dockerComposeFile` feature to compose two files:
1. The existing `docker-compose.yml` (Postgres service, unchanged)
2. A new `docker-compose.devcontainer.yml` (app/dev service overlay)

VS Code is configured at workspace scope to use Podman via `dev.containers.dockerPath`.

---

## Files

### New files

| Path | Purpose |
|---|---|
| `.devcontainer/devcontainer.json` | VS Code dev container configuration |
| `.devcontainer/docker-compose.devcontainer.yml` | App service definition, extends existing compose |
| `.vscode/settings.json` | Workspace-scoped Podman socket path |

### Unchanged files

| Path | Reason |
|---|---|
| `docker-compose.yml` | Postgres config is reused as-is |

---

## Component Design

### `.devcontainer/docker-compose.devcontainer.yml`

- Defines an `app` service using `mcr.microsoft.com/devcontainers/dotnet:1-10.0` (official .NET 10 dev container image)
- Mounts repo root to `/workspaces/Pacevite` via bind mount
- Sets `command: sleep infinity` so the container stays alive for VS Code to attach
- `depends_on: db` so Postgres is healthy before the app container is ready
- Injects `ConnectionStrings__DefaultConnection` env var pointing to `db:5432` (Compose internal network)
- Connects to the same Compose network as `db`

### `.devcontainer/devcontainer.json`

```json
{
  "name": "Pacevite",
  "dockerComposeFile": ["../docker-compose.yml", "docker-compose.devcontainer.yml"],
  "service": "app",
  "workspaceFolder": "/workspaces/Pacevite",
  "shutdownAction": "stopCompose",
  "postCreateCommand": "dotnet restore",
  "forwardPorts": [8080],
  "customizations": {
    "vscode": {
      "extensions": [
        "ms-dotnettools.csdevkit",
        "eamodio.gitlens",
        "humao.rest-client"
      ]
    }
  }
}
```

### `.vscode/settings.json`

```json
{
  "dev.containers.dockerPath": "/usr/bin/podman"
}
```

This is workspace-scoped — contributors on Podman pick it up automatically. Contributors on Docker can override it locally via user settings.

---

## Data Flow

```
VS Code opens folder
  → detects .devcontainer/devcontainer.json
  → prompts "Reopen in Container"
  → Podman reads both compose files
  → starts `db` (Postgres 17)
  → starts `app` (.NET 10 devcontainer image)
  → mounts repo root into /workspaces/Pacevite
  → runs `dotnet restore`
  → VS Code attaches to `app` container
  → developer runs dotnet run / dotnet test inside container
  → app reaches Postgres at db:5432 via Compose network
  → port 8080 forwarded to host
```

---

## Error Handling & Edge Cases

| Scenario | Handling |
|---|---|
| `DB_USER` / `DB_PASSWORD` not set | Postgres container fails to start with clear error from `pg_isready` healthcheck — contributor sees it in terminal |
| Podman not at `/usr/bin/podman` | Override `dev.containers.dockerPath` in user settings |
| Contributor using Docker | `dev.containers.dockerPath` workspace setting is ignored; Docker is used by default |
| Port 8080 already in use | VS Code prompts to remap the forwarded port |
| Container rebuild needed after config change | Use "Dev Containers: Rebuild Container" from Command Palette |

---

## Testing Criteria

- [ ] VS Code prompts "Reopen in Container" when opening the project
- [ ] Both `db` and `app` containers start successfully under Podman
- [ ] `dotnet restore` completes in `postCreateCommand`
- [ ] `dotnet run` starts the API and port 8080 is accessible on the host
- [ ] `dotnet test` runs and reaches the Postgres `db` service
- [ ] C# Dev Kit, GitLens, and REST Client extensions are installed in the container
- [ ] Closing the remote window stops both containers (`shutdownAction: stopCompose`)
