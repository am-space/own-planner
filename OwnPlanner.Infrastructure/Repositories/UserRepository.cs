using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for User entity using Entity Framework Core.
/// </summary>
public class UserRepository(AuthDbContext context)
	: RepositoryBase<User, AuthDbContext>(context), IUserRepository
{
	public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
		=> await Set.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

	public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		return await Set.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
	}

	public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		return await Set.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
	}

	public async Task<int> GetRegisteredUserCountAsync(CancellationToken cancellationToken = default)
		=> await Set.CountAsync(cancellationToken);

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var user = await GetByIdAsync(id, cancellationToken);
		if (user != null)
			await base.DeleteAsync(user, cancellationToken);
	}

	async Task<User> IUserRepository.AddAsync(User user, CancellationToken cancellationToken)
	{
		Set.Add(user);
		await Db.SaveChangesAsync(cancellationToken);
		return user;
	}

	async Task<User> IUserRepository.UpdateAsync(User user, CancellationToken cancellationToken)
	{
		Set.Update(user);
		await Db.SaveChangesAsync(cancellationToken);
		return user;
	}
}
