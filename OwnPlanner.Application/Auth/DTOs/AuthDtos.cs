namespace OwnPlanner.Application.Auth.DTOs;

public record RegisterRequest(
	string Email,
	string Username,
	string Password
);

public record LoginRequest(
	string Email,
	string Password
);

public record UserResponse(
	Guid Id,
	string Email,
	string Username,
	DateTime CreatedAt,
	DateTime? LastLoginAt
);

public record AuthResult(
	bool Success,
	string? ErrorMessage = null,
	UserResponse? User = null
);
