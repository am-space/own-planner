using System.Collections.Concurrent;

namespace OwnPlanner.Web.Server.Services;

public sealed class TelegramChatLock
{
	private readonly ConcurrentDictionary<long, Entry> _locks = new();

	public async ValueTask<IDisposable> AcquireAsync(long chatId, CancellationToken cancellationToken)
	{
		Entry entry;
		while (true)
		{
			entry = _locks.GetOrAdd(chatId, _ => new Entry());
			lock (entry)
			{
				if (_locks.TryGetValue(chatId, out var current) && ReferenceEquals(current, entry))
				{
					entry.References++;
					break;
				}
			}
		}

		try
		{
			await entry.Semaphore.WaitAsync(cancellationToken);
			return new Releaser(this, chatId, entry);
		}
		catch
		{
			ReleaseReference(chatId, entry, releaseSemaphore: false);
			throw;
		}
	}

	internal int EntryCount => _locks.Count;

	private void ReleaseReference(long chatId, Entry entry, bool releaseSemaphore)
	{
		if (releaseSemaphore) entry.Semaphore.Release();
		lock (entry)
		{
			entry.References--;
			if (entry.References == 0)
			{
				_locks.TryRemove(new KeyValuePair<long, Entry>(chatId, entry));
				entry.Semaphore.Dispose();
			}
		}
	}

	private sealed class Entry
	{
		public SemaphoreSlim Semaphore { get; } = new(1, 1);
		public int References { get; set; }
	}

	private sealed class Releaser(TelegramChatLock owner, long chatId, Entry entry) : IDisposable
	{
		private bool _disposed;
		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			owner.ReleaseReference(chatId, entry, releaseSemaphore: true);
		}
	}
}
