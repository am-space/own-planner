namespace OwnPlanner.Application.Auth;

public record CreatePersonalAccessTokenRequest(
	string Name
);

public record PersonalAccessTokenResponse(
	Guid Id,
	Guid UserId,
	string Name,
	DateTime CreatedAt,
	DateTime? LastUsedAt,
	DateTime? RevokedAt
);

public record PersonalAccessTokenCreatedResponse(
	PersonalAccessTokenResponse Token,
	string PlaintextToken
);
