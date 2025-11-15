# Chat API

The Chat API provides AI-powered conversational capabilities using Google's Gemini LLM with optional MCP (Model Context Protocol) tool integration.

## Key Features

### ?? Per-Login Session Isolation
- **Each login creates a unique chat session** identified by a session ID in the authentication cookie
- **Multiple logins = Multiple independent chat sessions** (different browsers, devices, or tabs)
- When a user logs in, a new session ID is generated and stored in the authentication cookie
- Each session has its own:
  - Conversation context (history)
  - MCP server process
  - Independent lifecycle

### ?? Per-User Data Isolation
- **Each user has their own SQLite database file** named `ownplanner-user-{userId}.db`
- Complete data isolation - users cannot access each other's tasks or notes
- Database files are created on first use and persist across sessions
- All MCP tools operate on the user-specific database
- Multiple sessions for the same user share the same database (task/note data)

### ?? Session Management
- **Session ID is unique per login** - generated as a GUID when user logs in
- Sessions persist across multiple requests (conversation context maintained)
- Inactive sessions are automatically cleaned up after 30 minutes
- Each session has its own MCP server process with user-specific database
- Logging out doesn't automatically clean up the session (timeout handles it)

## Authentication & Session Flow

### Login Process
```
User logs in ? POST /api/auth/login
    ?
Auth cookie created with claims:
    - UserId: "abc-123"
    - SessionId: "e4d2c9f8-..." (unique GUID)
    - Username, Email
    ?
Cookie sent to browser (7-day expiration)
```

### First Chat Message
```
POST /api/chat/message
    ?
ChatController extracts from cookie:
    - SessionId: "e4d2c9f8-..."
    - UserId: "abc-123"
    ?
ChatSessionManager creates session:
    - Key: "e4d2c9f8-..."
    - Launches MCP with: --session-id e4d2c9f8-... --user-id abc-123
    - Database: ownplanner-user-abc-123.db
```

### Multiple Logins Scenario
```
User logs in from Browser A
    SessionId: "aaaa-1111"
    Chat Session: "aaaa-1111" ? MCP Process A ? user-abc-123.db

User logs in from Browser B
    SessionId: "bbbb-2222"
    Chat Session: "bbbb-2222" ? MCP Process B ? user-abc-123.db

Result:
? Two independent chat conversations
? Two separate MCP server processes
? Same user database (shared task/note data)
? Different conversation contexts
```

## Configuration

Add your Gemini API key and MCP settings to `appsettings.Development.json`:

```json
{
  "Chat": {
    "Gemini": {
      "ApiKey": "YOUR_GEMINI_API_KEY_HERE",
      "Model": "gemini-2.0-flash-exp",
      "MaxToolCallRounds": 10
    },
    "Mcp": {
      "Command": "dotnet",
      "Arguments": [
        "run",
        "--project",
        "..\\..\\OwnPlanner.Mcp.StdioApp\\OwnPlanner.Mcp.StdioApp.csproj"
      ]
    }
  }
}
```

### MCP Configuration

- **Command**: The command to launch the MCP server (e.g., "dotnet" or absolute path to .exe)
- **Arguments**: Array of arguments to pass to the command
- **Auto-appended**: `--session-id <session-id> --user-id <user-id>` are automatically added
- To disable MCP, set `Command` to an empty string

## API Endpoints

All endpoints require authentication (user must be logged in).

### POST `/api/chat/message`

Send a message to the AI and get a response.

**Request Body:**
```json
{
  "message": "Hello, how can you help me with my tasks?"
}
```

**Response:**
```json
{
  "message": "Hello! I can help you manage your tasks, notes, and more...",
  "sessionId": "e4d2c9f8-a1b2-4c3d-9e8f-7g6h5i4j3k2l",
  "timestamp": "2025-01-15T10:30:00Z"
}
```

**Status Codes:**
- `200 OK` - Success
- `400 Bad Request` - Invalid request (empty message)
- `401 Unauthorized` - User not authenticated
- `500 Internal Server Error` - Processing error

### POST `/api/chat/clear`

Clear the current session's chat (terminates MCP server and starts fresh on next message).

**Response:**
```json
{
  "message": "Chat session cleared",
  "sessionId": "e4d2c9f8-a1b2-4c3d-9e8f-7g6h5i4j3k2l"
}
```

**Status Codes:**
- `200 OK` - Session cleared successfully
- `401 Unauthorized` - User not authenticated
- `500 Internal Server Error` - Error clearing session

### GET `/api/chat/status`

Get the status of the current session.

**Response:**
```json
{
  "sessionId": "e4d2c9f8-a1b2-4c3d-9e8f-7g6h5i4j3k2l",
  "isActive": true,
  "activeSessionsCount": 5
}
```

**Status Codes:**
- `200 OK` - Success
- `401 Unauthorized` - User not authenticated

### GET `/api/chat/health`

Health check endpoint (no authentication required).

**Response:**
```json
{
  "status": "healthy",
  "activeSessions": 5,
  "timestamp": "2025-01-15T10:30:00Z"
}
```

**Status Codes:**
- `200 OK` - Service is healthy

## Architecture

### Per-Login Session Isolation

```
User Login (Browser A)
    ?
SessionId: "aaaa-1111" created
    ?
User sends message
    ?
ChatSessionManager["aaaa-1111"] created
    ?
MCP Server launches:
    --session-id aaaa-1111
    --user-id abc-123
    ?
Database: ownplanner-user-abc-123.db

User Login (Browser B) - SAME USER
    ?
SessionId: "bbbb-2222" created (NEW!)
    ?
User sends message
    ?
ChatSessionManager["bbbb-2222"] created (SEPARATE!)
    ?
MCP Server launches:
    --session-id bbbb-2222
    --user-id abc-123
    ?
Database: ownplanner-user-abc-123.db (SAME!)

Result:
? Two independent conversations
? Two MCP processes
? One shared database
```

### Components

1. **AuthController**
   - Generates unique `SessionId` (GUID) on login
   - Adds SessionId to authentication cookie claims
   - 7-day cookie expiration

2. **ChatController**
   - Extracts `SessionId` and `UserId` from authentication claims
   - Uses SessionId as the chat session key
   - Passes both to ChatSessionManager

3. **ChatSessionManager**
   - Keys sessions by SessionId (not userId)
   - Manages per-session chat resources
   - Handles automatic session cleanup (30 min timeout)
   - Thread-safe session access

4. **ChatServiceFactory**
   - Creates ChatServiceAdapter instances
   - Passes both SessionId and UserId to MCP server
   - Launches separate MCP process per session

5. **MCP Server** (`OwnPlanner.Mcp.StdioApp`)
   - Receives `--session-id` and `--user-id` via command-line
   - Uses UserId for database: `ownplanner-user-{userId}.db`
   - Enriches logs with both SessionId and UserId
   - Separate process per active session

### Session Identifier Strategy

**Session ID**: Unique per login (GUID from authentication cookie)
- Generated on each login
- Different for each browser/device/tab
- Identifies the chat session

**User ID**: Unique per user (from user account)
- Same across all logins
- Identifies the database file
- Used for data isolation

```
SessionId (per-login)     ?  Chat session key
UserId (per-user)         ?  Database file name
```

### Per-User Database Isolation

Each user gets their own SQLite database file:

```
AppContext.BaseDirectory/
??? ownplanner-user-abc-123.db  (User A's data)
?   ??? Accessed by SessionId: aaaa-1111 (Browser A)
?   ??? Accessed by SessionId: bbbb-2222 (Browser B)
?
??? ownplanner-user-xyz-789.db  (User B's data)
    ??? Accessed by SessionId: cccc-3333 (Browser C)
```

**Benefits:**
- ? Complete data isolation between users
- ? Shared data across user's sessions (tasks/notes)
- ? Independent conversation contexts per login
- ? No cross-user data leakage

### Session Lifecycle

```
Time: 0s - User logs in (Browser A)
    ?
    [SessionId Created: aaaa-1111]
    - Added to authentication cookie
    
Time: 0s - User sends first message
    ?
    [Chat Session Created]
    - Key: aaaa-1111
    - MCP Server launches
    - Conversation starts
    
Time: 0s-30min - User continues conversation
    ?
    [Session Active]
    - Same MCP server instance
    - Same database connection
    - Context maintained
    
Time: 10min - User logs in (Browser B)
    ?
    [SessionId Created: bbbb-2222]
    - New authentication cookie
    - New chat session (independent!)
    
Time: 30min - No activity on Browser A session
    ?
    [Session aaaa-1111 Expired]
    - Automatic cleanup timer runs
    - Session disposed
    - MCP server terminated
    
Browser B session (bbbb-2222) still active!
```

## Security

- All chat endpoints require authentication
- Sessions are isolated per login (using session ID from cookie)
- Each session has its own MCP server process
- Each user has their own database file
- Multiple logins from same user create independent sessions
- API keys are stored in configuration (not in source control)
- Health check endpoint is public for monitoring

## Example Usage

### JavaScript/TypeScript

```typescript
// Login to get session
const loginResponse = await fetch('/api/auth/login', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'user@example.com', password: 'password' })
});
// Authentication cookie set with unique SessionId

// Check auth status (includes sessionId)
const authCheck = await fetch('/api/auth/check');
const auth = await authCheck.json();
console.log(auth.sessionId); // e.g., "e4d2c9f8-..."

// Send a message (uses SessionId from cookie)
const response = await fetch('/api/chat/message', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ message: 'What tasks do I have today?' })
});

const data = await response.json();
console.log(data.message); // AI response
console.log(data.sessionId); // Same as auth.sessionId

// Clear this session's chat
await fetch('/api/chat/clear', { method: 'POST' });

// Check session status
const status = await fetch('/api/chat/status');
const statusData = await status.json();
console.log(`Session: ${statusData.sessionId}`);
console.log(`Active sessions: ${statusData.activeSessionsCount}`);
```

### Multi-Device Scenario

```typescript
// User opens two browser tabs or devices

// Tab 1: Login
const login1 = await fetch('/api/auth/login', { /* credentials */ });
// SessionId: "aaaa-1111" in cookie

// Tab 2: Login (same user)
const login2 = await fetch('/api/auth/login', { /* credentials */ });
// SessionId: "bbbb-2222" in cookie (DIFFERENT!)

// Tab 1: Chat about tasks
await fetch('/api/chat/message', {
  body: JSON.stringify({ message: 'Show me my tasks' })
});
// Uses session "aaaa-1111"

// Tab 2: Chat about something else
await fetch('/api/chat/message', {
  body: JSON.stringify({ message: 'Create a note about project ideas' })
});
// Uses session "bbbb-2222" (INDEPENDENT!)

// Both sessions work independently but share task/note data
```

## MCP Server Session Context

The MCP server receives both session ID and user ID:

```bash
# Command executed for session "e4d2c9f8-..." user "abc-123"
dotnet run --project ... --session-id e4d2c9f8-a1b2-4c3d-9e8f-7g6h5i4j3k2l --user-id abc-123
```

**SessionContext in MCP Server:**
```csharp
public class SessionContext
{
    public required string SessionId { get; init; }  // e.g., "e4d2c9f8-..."
    public required string UserId { get; init; }      // e.g., "abc-123"
}
```

## Troubleshooting

### "Session ID not found in claims"

This means the user needs to re-login:
- Session ID is added to cookie on login
- If missing, authentication cookie may be from before this feature
- User should logout and login again

### Multiple Active Sessions

This is expected and intentional:
- **Each login creates a new session**
- Same user can have multiple sessions (different browsers/devices)
- Use `/api/chat/status` to see total active sessions
- Each session is independent with its own conversation

### Shared Task/Note Data Across Sessions

This is correct behavior:
- Multiple sessions for same user share database
- User can modify tasks in Browser A
- Changes visible to MCP tools in Browser B
- Conversation context is separate, data is shared

### Session Timeout

Sessions automatically expire after 30 minutes of inactivity:
- MCP server process is terminated
- Next message creates new session (if cookie still valid)
- User database persists (data not lost)

## Logging

Chat operations are logged to:
- **Console**: INFO level and above
- **File**: `logs/web-{date}.log` - Web server logs with sessionId and userId
- **MCP Server**: `logs/stdioapp-{date}.log` - MCP server logs enriched with both IDs

Example log entries:
```
[INF] User logged in: UserId abc-123
[DBG] Created session ID for user abc-123: e4d2c9f8-a1b2-4c3d-9e8f-7g6h5i4j3k2l
[INF] Processing chat message for sessionId: e4d2c9f8-..., userId: abc-123
[INF] Creating new chat session: e4d2c9f8-... for user: abc-123
[INF] MCP Server starting - SessionId: e4d2c9f8-..., UserId: abc-123
[INF] Using database: /path/to/ownplanner-user-abc-123.db
