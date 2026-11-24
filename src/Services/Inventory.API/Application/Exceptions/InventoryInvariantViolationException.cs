namespace Inventory.API.Application.Exceptions;

public sealed class InventoryInvariantViolationException : Exception
{
    public InventoryInvariantViolationException(string message) : base(message)
    {
    }

    public InventoryInvariantViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}