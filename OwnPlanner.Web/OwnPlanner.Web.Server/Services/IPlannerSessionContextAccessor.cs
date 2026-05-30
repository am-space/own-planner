using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

public interface IPlannerSessionContextAccessor
{
	SessionContext? Current { get; }
	IDisposable BeginScope(SessionContext context);
}

