using Microsoft.EntityFrameworkCore;
using OwnPlanner.Domain.Users;
using OwnPlanner.Infrastructure.Persistence;

namespace OwnPlanner.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for User entity using Entity Framework Core.
/// </summary>
public class UserRepository : IUserRepository
{
	private readonly AuthDbContext _context;

	public UserRepository(AuthDbContext context)
	{
		_context = context;
	}

	public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		return await _context.Users
			.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
	}

	public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		return await _context.Users
			.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
	}

	public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		var normalizedEmail = email.Trim().ToLowerInvariant();
		return await _context.Users
			.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
	}

	public async Task<User> AddAsync(User user, CancellationToken cancellationToken = default)
	{
		_context.Users.Add(user);
		await _context.SaveChangesAsync(cancellationToken);
		return user;
	}

	public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
	{
		_context.Users.Update(user);
		await _context.SaveChangesAsync(cancellationToken);
		return user;
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var user = await GetByIdAsync(id, cancellationToken);
		if (user != null)
		{
			_context.Users.Remove(user);
			await _context.SaveChangesAsync(cancellationToken);
		}
	}
}
