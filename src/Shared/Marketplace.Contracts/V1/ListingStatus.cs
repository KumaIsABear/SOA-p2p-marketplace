using System.Text.Json.Serialization;

namespace Marketplace.Contracts.V1;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ListingStatus
{
    PendingLock,
    Active,
    Processing,
    Sold,
    Cancelled,
    CreateFailed
}
