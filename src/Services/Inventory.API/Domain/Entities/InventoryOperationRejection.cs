namespace Inventory.API.Domain.Entities;

public sealed class InventoryOperationRejection
{
    public Guid OperationId { get; set; }
    public Guid TradeId { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public string FailureCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}