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

Register and use chat to create tasks, this will spawn MCP processes

### Check database locations
```sh
docker exec test ls -la /app/data/databases/
```
Should show: ownplanner-user-{userId}.db files

### Check log locations
```sh
docker exec test ls -la /app/data/logs/
```
Should show: mcp-stdioapp-user-{userId}-*.log files
