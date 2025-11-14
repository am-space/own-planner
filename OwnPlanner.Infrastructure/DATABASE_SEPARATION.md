# Database Separation - Authentication and Application Data

## Overview

The OwnPlanner application uses two separate SQLite databases with a clear separation by project:

1. **`ownplanner-auth.db`** - User authentication (Web Server only)
2. **`ownplanner.db`** - Application data: tasks, notes, lists (MCP Server only)

## Project Database Usage

### Web Server (`OwnPlanner.Web.Server`)
- **Uses**: `AuthDbContext` ? `ownplanner-auth.db`
- **Purpose**: User authentication, registration, login
- **Entities**: `User`
- **Why**: The web server only handles user authentication and serves the frontend

### MCP Server (`OwnPlanner.Mcp.StdioApp`)
- **Uses**: `AppDbContext` ? `ownplanner.db`
- **Purpose**: Task and note management via MCP tools
- **Entities**: `TaskItem`, `TaskList`, `NoteItem`, `NoteList`
- **Why**: The MCP server handles all business logic for tasks and notes

This separation ensures:
- **Clear Responsibilities**: Each server handles its own domain
- **Independent Scaling**: Auth and data can scale separately
- **Security Isolation**: User credentials are isolated from business data
- **Simpler Configuration**: Each project only configures what it needs

## Database Contexts

### AuthDbContext
- **Purpose**: Handles authentication and user management
- **Entities**: `User`
- **Location**: `OwnPlanner.Infrastructure/Persistence/AuthDbContext.cs`
- **Migration Folder**: `OwnPlanner.Infrastructure/Migrations/AuthDb/`
- **Used By**: Web Server

### AppDbContext
- **Purpose**: Handles application business data
- **Entities**: `TaskItem`, `TaskList`, `NoteItem`, `NoteList`
- **Location**: `OwnPlanner.Infrastructure/Persistence/AppDbContext.cs`
- **Migration Folder**: `OwnPlanner.Infrastructure/Migrations/`
- **Used By**: MCP Server

## Configuration

### Web Server Configuration

```csharp
// OwnPlanner.Web.Server/Program.cs
var authDbPath = Path.Combine(builder.Environment.ContentRootPath, "ownplanner-auth.db");
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlite($"Data Source={authDbPath}")
);
```

### MCP Server Configuration

```csharp
// OwnPlanner.Mcp.StdioApp/Program.cs
var dbPath = Path.Combine(AppContext.BaseDirectory, "ownplanner.db");
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}")
);
```

## Working with Migrations

### Creating Migrations

**For AppDbContext (tasks, notes):**
```bash
cd OwnPlanner.Infrastructure
dotnet ef migrations add MigrationName --context AppDbContext
```

**For AuthDbContext (users, auth):**
```bash
cd OwnPlanner.Infrastructure
dotnet ef migrations add MigrationName --context AuthDbContext
```

### Applying Migrations

Migrations are automatically applied on application startup in `Program.cs` of each project:

```csharp
using (var scope = app.Services.CreateScope())
{
    var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    appDb.Database.Migrate();
    
    var authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    authDb.Database.Migrate();
}
```

### Manual Migration Application

If you need to manually apply migrations:

**For AppDbContext:**
```bash
cd OwnPlanner.Infrastructure
dotnet ef database update --context AppDbContext
```

**For AuthDbContext:**
```bash
cd OwnPlanner.Infrastructure
dotnet ef database update --context AuthDbContext
```

## Repository Updates

### UserRepository
- Now uses `AuthDbContext` instead of `AppDbContext`
- All user operations are isolated to the authentication database

### Other Repositories
- Continue to use `AppDbContext`
- No changes required for TaskItem, TaskList, NoteItem, NoteList repositories

## Benefits of Separation

1. **Security Isolation**: Authentication data is physically separated from business data
2. **Backup Strategy**: Can backup auth data more frequently or securely
3. **Scalability**: Different databases can be scaled independently
4. **Clear Boundaries**: Explicit separation of concerns in the architecture
5. **Migration Independence**: Auth and app migrations don't interfere with each other

## Database Files

When running the application, you'll see two database files:

- `ownplanner.db` - Your tasks, notes, and lists
- `ownplanner-auth.db` - User accounts and authentication data

## Testing

When writing tests, you may need to set up both contexts:

```csharp
// Example for integration tests
var appOptions = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite("DataSource=:memory:")
    .Options;
var appDb = new AppDbContext(appOptions);

var authOptions = new DbContextOptionsBuilder<AuthDbContext>()
    .UseSqlite("DataSource=:memory:")
    .Options;
var authDb = new AuthDbContext(authOptions);
```

## Design-Time Context Factories

Both contexts have design-time factories for EF Core tooling:

- `AppDbContextFactory` - For AppDbContext migrations
- `AuthDbContextFactory` - For AuthDbContext migrations

These factories are used by `dotnet ef` commands to create the contexts at design time.

## Migration History

### Initial Setup (November 14, 2024)
- Created `AuthDbContext` for user authentication
- Updated `AppDbContext` to remove User entity
- Created `InitialAuthDb` migration (AuthDbContext)
- Created `RemoveUsersFromAppDb` migration (AppDbContext - empty as Users weren't in DB yet)
- Updated `UserRepository` to use `AuthDbContext`
- Updated `Program.cs` to configure both databases

## Future Considerations

If you need to relate users to application data (e.g., task ownership):

1. Store UserId as a Guid in the application entities
2. Use repository pattern to fetch user details when needed
3. Avoid direct foreign key relationships between databases
4. Consider implementing user context/claims for the current user

## Troubleshooting

**Issue**: Migrations not found
- **Solution**: Ensure you're in the `OwnPlanner.Infrastructure` directory when running EF commands

**Issue**: Wrong context being used
- **Solution**: Always specify `--context AppDbContext` or `--context AuthDbContext` in EF commands

**Issue**: Database not created
- **Solution**: Check that both databases are initialized in `Program.cs` startup code

**Issue**: "No DbContext found" error
- **Solution**: Make sure design-time factories exist for both contexts
