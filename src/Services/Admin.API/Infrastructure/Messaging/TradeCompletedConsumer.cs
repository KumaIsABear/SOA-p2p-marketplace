using Admin.API.Application.Interfaces;
using Marketplace.Contracts.V1;
using MassTransit;

namespace Admin.API.Infrastructure.Messaging;

public sealed class TradeCompletedConsumer : IConsumer<TradeCompletedEvent> {
    private readonly IMarketAnalyticsService _analyticsService;
    private readonly ILogger<TradeCompletedConsumer> _logger;

    public TradeCompletedConsumer(
        IMarketAnalyticsService analyticsService,
        ILogger<TradeCompletedConsumer> logger
    ) {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TradeCompletedEvent> context) {
        _logger.LogInformation(
            "Consuming TradeCompletedEvent TradeId={TradeId} TemplateId={TemplateId} CorrelationId={CorrelationId}.",
            context.Message.TradeId,
            context.Message.TemplateId,
            context.Message.CorrelationId
        );

        await _analyticsService.RecordTradeCompletedAsync(context.Message, context.CancellationToken);
    }
}
