using Auth.API.Application.Common;
using Auth.API.Application.DTOs;

namespace Auth.API.Application.Interfaces;

public interface IAuthService {
    Task<AuthResult<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResult<TokenResponse>> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken);
    Task<AuthResult<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(RefreshRequest request, CancellationToken cancellationToken);
}
