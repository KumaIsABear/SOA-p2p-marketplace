using Gateway.API.Application.DTOs;

namespace Gateway.API.Application.Interfaces;

public enum AuthStatusLookupOutcome {
    Found,
    NotFound,
    Unavailable
}

public sealed record AuthStatusLookupResult(
    AuthStatusLookupOutcome Outcome,
    AccessStatusDto? Status = null
);

public interface IAuthStatusClient {
    Task<AuthStatusLookupResult> GetUserStatusAsync(Guid userId, string correlationId, CancellationToken cancellationToken);
    Task<bool> IsAuthReadyAsync(CancellationToken cancellationToken);
}
