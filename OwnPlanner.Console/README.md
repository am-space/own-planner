# OwnPlanner Console Chat

A console application that provides an interactive chat interface with Gemini LLM, with optional MCP (Model Context Protocol) support for accessing task management tools.

## Configuration

The application uses `appsettings.json` for configuration. For local development with secrets, create an `appsettings.Development.json` file (this file is gitignored).

### Setup

1. Copy `appsettings.json` to `appsettings.Development.json`
2. Edit `appsettings.Development.json` and add your Gemini API key:

```json
{
  "Gemini": {
    "ApiKey": "YOUR_API_KEY_HERE",
    "Model": "gemini-2.0-flash-exp"
  },
  "Mcp": {
    "Command": "dotnet",
    "Arguments": [
      "run",
      "--project",
      "..\\..\\..\\..\\OwnPlanner.Mcp.StdioApp\\OwnPlanner.Mcp.StdioApp.csproj"
    ]
  }
}
```

**Alternative: Using built executable (Windows)**
```json
{
  "Mcp": {
  "Command": "C:\\Users\\YourName\\source\\repos\\OwnPlanner\\OwnPlanner.Mcp.StdioApp\\bin\\Debug\\net9.0\\OwnPlanner.Mcp.StdioApp.exe",
    "Arguments": []
  }
}
```

**Note for Windows users**: Use backslashes (`\`) in paths. Each backslash must be escaped as `\\` in JSON.

### Getting a Gemini API Key

1. Visit https://makersuite.google.com/app/apikey
2. Create or select a Google Cloud project
3. Generate an API key
4. Copy the key to your `appsettings.Development.json`

### Configuration Options

#### Gemini Settings
- `ApiKey`: Your Gemini API key (required)
- `Model`: The Gemini model to use (default: "gemini-2.0-flash-exp")

#### MCP Settings
- `Command`: The command to launch the MCP server (e.g., "dotnet" or absolute path to .exe)
- `Arguments`: Array of arguments to pass to the command

**Path Guidelines:**
- Use **absolute paths** for direct executable execution on Windows
- Use **Windows backslashes** (`\\` in JSON) for paths
- Use `dotnet run --project` for a portable solution that works without building
- When running from `bin\Debug\net9.0`, relative paths go up 4 levels to reach sibling projects

To disable MCP, set `Command` to an empty string.

## Logging

The application uses Serilog for structured logging. Logs are written to:
- **Console**: All log levels displayed in the console
- **File**: `logs/console-{date}.log` - Rolling daily log files

Log files help diagnose issues with MCP initialization, tool execution, and Gemini API interactions. Check the logs directory if you encounter problems.

## Usage

```bash
dotnet run --project OwnPlanner.Console
```

Type your messages and press Enter to chat with Gemini. Type `exit` to quit.

## Troubleshooting

### MCP Initialization Failed

If you see "Warning: MCP initialization failed", check:

1. **Path Issues (Windows)**
   - Error: `'..' is not recognized as an internal or external command`
   - Solution: Use Windows-style paths with backslashes (`\\` in JSON) or absolute paths
   - Check the logs to see the "Current Directory" and verify relative paths from there

2. **MCP Server Build**
   - Ensure `OwnPlanner.Mcp.StdioApp` is built: `dotnet build OwnPlanner.Mcp.StdioApp`
   - Or use `dotnet run --project` approach which builds automatically

3. **Detailed Diagnostics**
   - Check the log file in `logs/` directory for detailed error messages
   - The log shows the exact command being executed and any stderr output

The application will continue to work without MCP tools if initialization fails.

## Security Note

**Never commit `appsettings.Development.json` to version control.** This file contains your API key and is automatically excluded via `.gitignore`.
