# OwnPlanner Web Server

ASP.NET Core 9 web server with React (Vite) frontend for the OwnPlanner application.

## Configuration

### Logging

The application uses Serilog for structured logging. Logs are written to:
- **Console**: All log levels displayed in the console output
- **File**: `logs/web-{date}.log` - Rolling daily log files

Log configuration in `Program.cs`:
- Default minimum level: `Debug`
- Microsoft framework logs: `Information` (filtered to reduce noise)
- ASP.NET Core logs: `Warning` (to show only important framework messages)
- Request logging enabled via `UseSerilogRequestLogging()`

### Global Exception Handling

The application uses a global exception handler (`GlobalExceptionHandler`) that:
- **Catches all unhandled exceptions** in the request pipeline
- **Logs exceptions** with full context (path, method, message)
- **Returns RFC 7807 Problem Details** responses
- **Maps exceptions to appropriate HTTP status codes**:
  - `KeyNotFoundException` ? 404 Not Found
  - `ArgumentException` / `ArgumentNullException` ? 400 Bad Request
  - `UnauthorizedAccessException` ? 401 Unauthorized
  - `InvalidOperationException` ? 400 Bad Request
  - All others ? 500 Internal Server Error

**Development vs Production**:
- **Development**: Full exception details including stack trace and inner exceptions
- **Production**: Generic error messages to avoid leaking sensitive information

**Example Error Response**:
```json
{
  "type": "https://httpstatuses.io/404",
  "title": "Resource Not Found",
  "status": 404,
  "detail": "Resource with ID '12345' was not found",
  "instance": "/api/tasks/12345"
}
```

**Testing Exception Handler**:
Use the `TestExceptionController` endpoints (remove in production):
- `GET /api/testexception/throw-exception` - 500 Internal Server Error
- `GET /api/testexception/not-found` - 404 Not Found
- `GET /api/testexception/bad-request` - 400 Bad Request
- `GET /api/testexception/unauthorized` - 401 Unauthorized
- `GET /api/testexception/success` - 200 OK

### Development

The server is configured with:
- **HTTPS** redirection enabled
- **OpenAPI/Swagger** available in Development mode at `/openapi/v1.json`
- **SPA Proxy** for React development server at `https://localhost:56404`
- **Static files** served from wwwroot (production builds)
- **Fallback routing** to `index.html` for SPA client-side routing
- **Global exception handling** with detailed errors in development

## Running the Application

### Development Mode
```bash
dotnet run --project OwnPlanner.Web.Server
```

This will:
1. Start the ASP.NET Core server (default: `https://localhost:7033`)
2. Automatically launch the React dev server via SPA Proxy (`https://localhost:56404`)
3. Log all requests and application events

### Production Build
```bash
dotnet publish -c Release
```

## Project Structure

```
OwnPlanner.Web.Server/
??? Controllers/          # API controllers
??? Middleware/          # Custom middleware (exception handler)
??? wwwroot/             # Static files (production React build)
??? Program.cs           # Application entry point & configuration
??? appsettings.json     # Application settings
??? OwnPlanner.Web.Server.csproj
```

## Next Steps

To complete the web application setup:
1. Add project references to Application and Infrastructure layers
2. Configure Entity Framework Core with SQLite
3. Register repositories and services in DI container
4. Create API controllers for Tasks and Notes
5. Configure CORS policy for development
6. Add authentication/authorization
7. **Remove TestExceptionController before production deployment**

## Troubleshooting

### Check Logs
If you encounter issues, check the log files in the `logs/` directory for detailed error messages and request traces.

### Exception Handling
All unhandled exceptions are caught by the global exception handler and logged. Check:
1. The HTTP response for a Problem Details JSON object
2. The log files for the full exception with stack trace
3. In development, the exception details are included in the response

### Port Conflicts
If ports 7033 (server) or 56404 (client dev) are in use:
- Server port can be changed in `Properties/launchSettings.json`
- Client dev port is configured in the client's `vite.config.ts` (DEV_SERVER_PORT)

### SPA Proxy Issues
If the React dev server doesn't start automatically:
1. Check that Node.js and npm are installed
2. Navigate to `../ownplanner.web.client` and run `npm install`
3. Verify the `SpaProxyLaunchCommand` in the `.csproj` file

## Security Notes

- User Secrets are enabled (ID: `226ea1d8-ef92-4c95-a0d1-4d75c5175a9f`)
- Development settings with secrets should be stored in User Secrets or `appsettings.Development.json` (gitignored)
- Never commit sensitive data like API keys or connection strings to version control
- **Remove TestExceptionController before deploying to production**
- Production error responses do not include sensitive exception details
