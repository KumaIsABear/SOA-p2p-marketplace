using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Auth.API.Domain.Enums;

namespace Auth.API.Application.DTOs;

public sealed record LoginRequest(
    [param: Required, EmailAddress, MaxLength(256)] string Email,
    [param: Required, MinLength(1), MaxLength(256)] string Password
);

public sealed record GoogleLoginRequest(
    [param: Required, MinLength(1)] string IdToken,
    [param: MaxLength(256)] string? Nonce
);

public sealed record RefreshRequest(
    [param: Required, MinLength(1), MaxLength(512)] string RefreshToken
);

public sealed record UserDto(
    Guid UserId,
    string Email,
    string DisplayName,
    UserRole Role,
    AccountStatus Status,
    long Version
);

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    UserDto User
);

public sealed record HealthDto(string Status);

public sealed record ApiProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string Code,
    string TraceId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Errors = null
);
