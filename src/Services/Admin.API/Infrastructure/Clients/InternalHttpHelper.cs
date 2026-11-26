using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Admin.API.Application.DTOs;
using Admin.API.Application.Interfaces;

namespace Admin.API.Infrastructure.Clients;

internal static class InternalHttpHelper {
    public static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<InternalCallResult<T>> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    ) {
        if (response.IsSuccessStatusCode) {
            var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            if (payload is null) {
                return new InternalCallResult<T>(InternalCallOutcome.Unavailable);
            }

            return new InternalCallResult<T>(InternalCallOutcome.Success, payload);
        }

        ApiProblemDetails? problem = null;
        try {
            problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(JsonOptions, cancellationToken);
        } catch {
            problem = null;
        }

        return response.StatusCode switch {
            HttpStatusCode.NotFound => new InternalCallResult<T>(InternalCallOutcome.NotFound, default, problem?.Code, problem?.Detail),
            HttpStatusCode.BadRequest => new InternalCallResult<T>(InternalCallOutcome.BadRequest, default, problem?.Code, problem?.Detail),
            HttpStatusCode.Conflict => new InternalCallResult<T>(InternalCallOutcome.Conflict, default, problem?.Code, problem?.Detail),
            _ => new InternalCallResult<T>(InternalCallOutcome.Unavailable, default, problem?.Code, problem?.Detail)
        };
    }
}
