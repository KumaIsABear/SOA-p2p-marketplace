namespace Gateway.API.Application.DTOs;

public sealed record AccessStatusDto(
    Guid UserId,
    string Role,
    string Status,
    bool CanTrade,
    long Version
);
