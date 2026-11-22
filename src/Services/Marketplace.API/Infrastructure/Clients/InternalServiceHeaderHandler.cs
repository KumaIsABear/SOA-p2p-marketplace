using Marketplace.API.Application.Options;
using Marketplace.API.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Marketplace.API.Infrastructure.Clients;

public sealed class InternalServiceHeaderHandler : DelegatingHandler
{
    private readonly InternalServicesOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public InternalServiceHeaderHandler(
        IOptions<InternalServicesOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceKey))
        {
            request.Headers.Remove("X-Service-Key");
            request.Headers.Add("X-Service-Key", _options.ServiceKey);
        }

        var correlationId = _httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey]?.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.Remove(CorrelationIdMiddleware.HeaderName);
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
