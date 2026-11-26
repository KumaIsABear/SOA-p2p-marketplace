namespace Admin.API.Application.Options;

public sealed class AnalyticsOptions {
    public const string SectionName = "Analytics";

    public string TimeZoneId { get; init; } = "Asia/Ho_Chi_Minh";
    public string DailySummaryTime { get; init; } = "00:00";
}
