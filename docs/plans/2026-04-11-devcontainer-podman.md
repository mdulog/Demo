# Dev Container + Podman Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Configure a VS Code dev container for the Pacevite .NET 10 API that works with Podman on Fedora, automatically starting Postgres alongside the app container.

**Architecture:** Two Compose files are merged — the existing `docker-compose.yml` (Postgres) stays untouched; a new `docker-compose.devcontainer.yml` adds the app/dev service. `devcontainer.json` references both. VS Code is told to use Podman via a workspace-scoped setting.

**Tech Stack:** VS Code Dev Containers extension, Podman, Docker Compose v2, .NET 10 SDK, Postgres 17

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| Create | `.devcontainer/devcontainer.json` | VS Code dev container entrypoint — references both compose files, sets extensions, lifecycle hooks |
| Create | `.devcontainer/docker-compose.devcontainer.yml` | Defines `app` service; mounts repo, overrides connection string host, depends on `db` |
| Create | `.vscode/settings.json` | Workspace-scoped Podman path |
| Create | `.env.example` | Documents required env vars for contributors |

---

## Task 1: Create `.env.example`

**Files:**
- Create: `.env.example`

> The existing `docker-compose.yml` requires `DB_USER` and `DB_PASSWORD` from a `.env` file (already gitignored). Contributors need to know what to put in it.

- [ ] **Step 1: Create `.env.example`**

```bash
# .env.example
DB_USER=pacevite_user
DB_PASSWORD=dev_password_123
```

Create the file at the repo root with exactly that content.

- [ ] **Step 2: Verify `.env` is gitignored**

Run:
```bash
grep "\.env" .gitignore
```

Expected output includes `.env`. If not present, add `.env` to `.gitignore`.

- [ ] **Step 3: Create your local `.env` from the example**

```bash
cp .env.example .env
```

- [ ] **Step 4: Commit**

```bash
git add .env.example
git commit -m "chore: add .env.example for dev container setup"
```

---

## Task 2: Create `docker-compose.devcontainer.yml`

**Files:**
- Create: `.devcontainer/docker-compose.devcontainer.yml`

> This is the Compose overlay for the dev container. It defines the `app` service using the official .NET 10 devcontainer image, mounts the repo, and overrides `ConnectionStrings__Default` to replace `localhost` with `db` (the Compose service name for Postgres). Without this override, the app inside the container can't reach Postgres because `localhost` inside the container is the container itself, not the Postgres service.

- [ ] **Step 1: Create the directory**

```bash
mkdir -p .devcontainer
```

- [ ] **Step 2: Create `.devcontainer/docker-compose.devcontainer.yml`**

```yaml
services:
  app:
    image: mcr.microsoft.com/devcontainers/dotnet:1-10.0
    volumes:
      - ..:/workspaces/Pacevite:cached
    command: sleep infinity
    depends_on:
      db:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Default: "Host=db;Port=5432;Database=pacevite;Username=${DB_USER:-pacevite_user};Password=${DB_PASSWORD:-dev_password_123}"
```

> **Note on image tag:** `mcr.microsoft.com/devcontainers/dotnet:1-10.0` is the .NET 10 devcontainer image. If this tag does not yet exist (check `mcr.microsoft.com/devcontainers/dotnet` tags), substitute `mcr.microsoft.com/dotnet/sdk:10.0` and add a `user: vscode` entry — or use `1-9.0` temporarily until the 10.0 tag publishes.

> **Note on `${DB_USER:-pacevite_user}`:** The `:-` syntax provides a fallback default if the env var is unset. This means `.env` is optional for local dev — the defaults match `appsettings.Development.json`.

- [ ] **Step 3: Verify compose file syntax**

```bash
podman compose -f docker-compose.yml -f .devcontainer/docker-compose.devcontainer.yml config
```

Expected: merged YAML output with no errors. Confirm `app.depends_on.db.condition` is `service_healthy` and `app.environment.ConnectionStrings__Default` contains `Host=db`.

- [ ] **Step 4: Commit**

```bash
git add .devcontainer/docker-compose.devcontainer.yml
git commit -m "chore: add devcontainer compose overlay for app service"
```

---

## Task 3: Create `devcontainer.json`

**Files:**
- Create: `.devcontainer/devcontainer.json`

> This is the VS Code dev container entrypoint. It wires together the two compose files, declares the `app` service as the container to attach to, sets the workspace folder inside the container, installs VS Code extensions, and runs `dotnet restore` on first open.

- [ ] **Step 1: Create `.devcontainer/devcontainer.json`**

```json
{
  "name": "Pacevite",
  "dockerComposeFile": ["../docker-compose.yml", "docker-compose.devcontainer.yml"],
  "service": "app",
  "runServices": ["db"],
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

> **`runServices: ["db"]`** — explicitly tells VS Code to also start the `db` service (in addition to `app`). Without this, only `app` starts; `depends_on` still works but `runServices` makes the intent explicit and ensures `db` is listed in the compose lifecycle.

> **`shutdownAction: "stopCompose"`** — stops both `app` and `db` when you close the remote window. Prevents Postgres from lingering in the background.

> **`forwardPorts: [8080]`** — forwards port 8080 from the container to your host. The .NET development server defaults to 8080 in .NET 8+.

- [ ] **Step 2: Validate JSON syntax**

```bash
cat .devcontainer/devcontainer.json | python3 -m json.tool > /dev/null && echo "Valid JSON"
```

Expected: `Valid JSON`

- [ ] **Step 3: Commit**

```bash
git add .devcontainer/devcontainer.json
git commit -m "chore: add devcontainer.json for VS Code dev container support"
```

---

## Task 4: Create `.vscode/settings.json`

**Files:**
- Create: `.vscode/settings.json`

> VS Code's Dev Containers extension looks for Docker at `/var/run/docker.sock` by default. On Fedora with Podman, there is no Docker daemon. This setting tells the extension to call `podman` directly instead. It is workspace-scoped so it travels with the repo — contributors using Podman pick it up automatically. Contributors using Docker can override it in their user settings without touching this file.

- [ ] **Step 1: Create `.vscode/settings.json`**

```json
{
  "dev.containers.dockerPath": "/usr/bin/podman"
}
```

> If `/usr/bin/podman` is not the correct path on your system, run `which podman` to find it.

- [ ] **Step 2: Verify Podman is at that path**

```bash
which podman
```

Expected: `/usr/bin/podman`. If different, update the path in `settings.json`.

- [ ] **Step 3: Commit**

```bash
git add .vscode/settings.json
git commit -m "chore: configure VS Code to use Podman for dev containers"
```

---

## Task 5: Verify the Dev Container Works End-to-End

> This task is manual verification. No code is written.

- [ ] **Step 1: Open the project in VS Code**

Open the `Pacevite` folder in VS Code. You should see a notification in the bottom-right corner:

> "Folder contains a Dev Container configuration file. Reopen in Container?"

Click **"Reopen in Container"**.

- [ ] **Step 2: Watch the build log**

VS Code opens a terminal showing the container startup log. Confirm:
- Podman pulls `mcr.microsoft.com/devcontainers/dotnet:1-10.0` (or uses cached)
- `db` (Postgres 17) starts and passes its healthcheck
- `app` container starts
- `dotnet restore` runs and succeeds

- [ ] **Step 3: Verify .NET SDK inside the container**

In the VS Code integrated terminal (now running inside the container):

```bash
dotnet --version
```

Expected: `10.0.x`

- [ ] **Step 4: Verify Postgres is reachable**

```bash
dotnet run --project src/Pacevite.Api/
```

Expected: API starts without connection errors. Look for output like:

```
Now listening on: http://0.0.0.0:8080
```

- [ ] **Step 5: Verify port forwarding**

In your browser on the host, navigate to `http://localhost:8080/scalar/v1` (the Scalar API explorer). Expected: Scalar UI loads.

- [ ] **Step 6: Verify tests reach the database**

```bash
dotnet run --project tests/Pacevite.Api.Tests/ -- --treenode-filter "/*/*/*/*[Category=Integration]"
```

Expected: integration tests pass (they require a live Postgres connection).

- [ ] **Step 7: Verify shutdown**

Close the VS Code remote window (File → Close Remote Connection). Then run:

```bash
podman ps
```

Expected: neither `pacevite_app` nor `pacevite_db` containers are running.

---

## Troubleshooting Reference

| Symptom | Fix |
|---|---|
| "Reopen in Container" prompt doesn't appear | Install the [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers) in VS Code |
| `image not found: mcr.microsoft.com/devcontainers/dotnet:1-10.0` | Tag doesn't exist yet — use `mcr.microsoft.com/dotnet/sdk:10.0` in `docker-compose.devcontainer.yml` |
| `db` healthcheck fails / Postgres doesn't start | Ensure `.env` exists with `DB_USER` and `DB_PASSWORD` matching `appsettings.Development.json` |
| `ConnectionStrings__Default` still points to `localhost` | The env var override isn't being picked up — confirm `ASPNETCORE_ENVIRONMENT=Development` is set and the compose overlay is being read |
| Podman not found at `/usr/bin/podman` | Run `which podman`, update `.vscode/settings.json` with the correct path |
