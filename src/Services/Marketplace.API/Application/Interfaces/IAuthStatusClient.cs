using Marketplace.API.Application.DTOs;

namespace Marketplace.API.Application.Interfaces;

public interface IAuthStatusClient
{
    Task<AuthAccessStatusDto> GetAccessStatusAsync(Guid userId, CancellationToken ct);
}
