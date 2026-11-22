namespace Admin.API.Application.Options;

public sealed class EconomyOptions {
    public const string SectionName = "Economy";

    public int DefaultBandBps { get; init; } = 2000;
}
