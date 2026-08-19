using System.Collections.Concurrent;

namespace OwnPlanner.Web.Server.Services;

public sealed class TelegramChatLock
{
	private readonly ConcurrentDictionary<long, SemaphoreSlim> _locks = new();

	public async ValueTask<IDisposable> AcquireAsync(long chatId, CancellationToken cancellationToken)
	{
		var semaphore = _locks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
		await semaphore.WaitAsync(cancellationToken);
		return new Releaser(semaphore);
	}

	private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
	{
		public void Dispose() => semaphore.Release();
	}
}
