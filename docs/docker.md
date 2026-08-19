# How to build and use Docker container

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
