namespace OwnPlanner.Application.Inbox;

public interface IInboxSeeder
{
	Task SeedAsync(CancellationToken ct = default);
}
