using Prometheus;

namespace Wallet.API.Infrastructure.Metrics;

public interface IWalletMetrics
{
    void RecordPaymentCallback(string outcome);
}

public sealed class WalletMetrics : IWalletMetrics
{
    public static class CallbackOutcomes
    {
        public const string Accepted = "accepted";
        public const string Duplicate = "duplicate";
        public const string Invalid = "invalid";
        public const string Retry = "retry";
    }

    private static readonly Counter PaymentCallbacksCounter = Prometheus.Metrics.CreateCounter(
        "marketplace_payment_callbacks_total",
        "Total number of ZaloPay payment callbacks processed by Wallet.API.",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    public void RecordPaymentCallback(string outcome)
    {
        PaymentCallbacksCounter.WithLabels(outcome).Inc();
    }
}