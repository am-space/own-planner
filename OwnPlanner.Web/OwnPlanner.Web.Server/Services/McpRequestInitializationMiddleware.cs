namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Ensures per-user planner database initialization before handling authenticated MCP HTTP requests.
/// </summary>
public sealed class McpRequestInitializationMiddleware(PerUserAppInitializationService initializationService)
	: IMiddleware
{
	public async Task InvokeAsync(HttpContext context, RequestDelegate next)
	{
		if (context.User.Identity?.IsAuthenticated == true)
		{
			await initializationService.EnsureInitializedAsync(
				context.User.GetRequiredPlannerSessionContext("mcp"),
				context.RequestAborted).ConfigureAwait(false);
		}

		await next(context).ConfigureAwait(false);
	}
}
