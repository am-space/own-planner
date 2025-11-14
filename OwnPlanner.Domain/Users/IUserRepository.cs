namespace OwnPlanner.Domain.Users;

/// <summary>
/// Repository interface for User entity operations.
/// </summary>
public interface IUserRepository
{
	Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
	Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
	Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
	Task<User> AddAsync(User user, CancellationToken cancellationToken = default);
	Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);
	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
