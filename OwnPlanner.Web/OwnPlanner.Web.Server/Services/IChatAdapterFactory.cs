using OwnPlanner.Application.Chat;

namespace OwnPlanner.Web.Server.Services;

/// <summary>
/// Creates the conversational-model adapter used by a web chat session.
/// This composition boundary lets alternate hosts replace the external model while preserving
/// the web session, planning, and tool-execution paths.
/// </summary>
public interface IChatAdapterFactory
{
	/// <summary>
	/// Creates an adapter for a chat session and gives it access to the session's initialized tool adapter.
	/// </summary>
	/// <param name="mcpAdapter">
	/// The tool adapter for the current session, or <see langword="null"/> when tool initialization failed.
	/// On a successful return, ownership transfers to the created chat adapter, which must dispose it
	/// from <see cref="IAsyncDisposable.DisposeAsync"/>.
	/// </param>
	/// <returns>A conversational-model adapter owned by the resulting planning service.</returns>
	IChatAdapter Create(IMcpAdapter? mcpAdapter);
}
