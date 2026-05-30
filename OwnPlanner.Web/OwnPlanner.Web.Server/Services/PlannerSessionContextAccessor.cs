using OwnPlanner.Mcp.Tools;

namespace OwnPlanner.Web.Server.Services;

public sealed class PlannerSessionContextAccessor : IPlannerSessionContextAccessor
{
	private readonly AsyncLocal<Holder?> _current = new();

	public SessionContext? Current => _current.Value?.Context;

	public IDisposable BeginScope(SessionContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		var previous = _current.Value;
		_current.Value = new Holder(context);
		return new RestoreScope(this, previous);
	}

	private sealed record Holder(SessionContext Context);

	private sealed class RestoreScope(PlannerSessionContextAccessor accessor, Holder? previous) : IDisposable
	{
		private readonly PlannerSessionContextAccessor _accessor = accessor;
		private readonly Holder? _previous = previous;
		private bool _disposed;

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_accessor._current.Value = _previous;
			_disposed = true;
		}
	}
}

