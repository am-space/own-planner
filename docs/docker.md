# How to build and use Docker container

## Headless local deployment

The repository Compose configuration builds the production image and runs it as a disposable local
deployment. It binds only to loopback by default, persists SQLite and logs in named volumes, and
uses ASP.NET Core's Development environment so cookie authentication works over local HTTP. Do not
expose this profile directly to an untrusted network.

```sh
cp .env.example .env
./scripts/docker-up.sh
```

Open `http://127.0.0.1:8080`, or set `OWNPLANNER_PORT` in `.env`. Stop the application while keeping
its data:

```sh
./scripts/docker-down.sh
```

Remove the disposable data and log volumes as well:

```sh
./scripts/docker-down.sh --volumes
```

The Compose file does not require a graphical desktop. Browser verification runs Chromium in
headless mode. `GEMINI_API_KEY` may be left empty for deployment smoke testing; only the explicit
live-AI workflow requires it.

## Build image
```sh
docker build -t ownplanner:latest -f OwnPlanner.Web/OwnPlanner.Web.Server/Dockerfile .
```

## Save image to tar file (optional)
```sh
docker save -o ownplanner_latest.tar ownplanner:latest
```

## Run container
```sh
docker run -d --name test -p 8080:8080 -e Chat__Gemini__ApiKey=YOUR_KEY ownplanner:latest
```

Register and use chat to create tasks; the web server executes planner tools in-process.

To enable the private Telegram channel, also supply `Telegram__Enabled=true`,
`Telegram__BotToken`, `Telegram__BotUsername`, and `Telegram__WebhookSecret`. Register the public
`https://<host>/api/telegram/webhook` endpoint with Telegram separately; see
[`telegram-integration.md`](telegram-integration.md). Never place these secrets in the image or
checked-in settings.

### Check database locations
```sh
docker exec test ls -la /app/data/databases/
```
Should show: ownplanner-user-{userId}.db files

### Check log locations
```sh
docker exec test ls -la /app/logs/
```
Should show: web-*.log files

## Agent-oriented deployment tests

Run the deterministic black-box test against a newly built container:

```sh
./scripts/docker-smoke-test.sh
```

The script waits for the container health check, verifies registration, authenticated navigation,
logout, and protected-route redirection in headless Chromium, and then removes the container and
its disposable volumes. On failure it retains the Playwright screenshot/trace under
`TestResults/Deployment/` and container logs in `TestResults/deployment-container.log`.

The separately authorized live Gemini scenario verifies tool selection by asking the assistant to
create a uniquely named Inbox task and then asserting the persisted task through the planner UI:

```sh
export GEMINI_API_KEY='your-key'
./scripts/docker-live-ai-test.sh
```

The live test defaults to the stable `gemini-3.5-flash-lite` model. Override it explicitly when
comparing another model:

```sh
export GEMINI_MODEL='gemini-3.5-flash-lite'
```

The live test is intentionally excluded from normal verification because provider behavior is
nondeterministic and incurs API usage. The scripts never print the key; keep it in the process
environment or an ignored `.env`, not in command arguments or tracked files.

To target an already-running deployment directly:

```sh
OWNPLANNER_BASE_URL=http://127.0.0.1:8080 \
  dotnet test OwnPlanner.Deployment.Tests --filter 'Category=DeploymentSmoke'
```

For a local HTTPS endpoint with a development certificate, explicitly set
`OWNPLANNER_IGNORE_HTTPS_ERRORS=true`. Never use that setting against a production endpoint.
