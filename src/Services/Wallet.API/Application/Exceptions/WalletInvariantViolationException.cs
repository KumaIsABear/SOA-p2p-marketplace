namespace Wallet.API.Application.Exceptions;

public sealed class WalletInvariantViolationException : Exception
{
    public WalletInvariantViolationException(string message)
        : base(message)
    {
    }
}