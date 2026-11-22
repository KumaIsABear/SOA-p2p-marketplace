namespace Wallet.API.Application.DTOs;

public sealed record TopUpPackageDto(
    string PackageId,
    long AmountVnd,
    long GemAmount
);

public sealed record TopUpPackagesResponse(
    IReadOnlyList<TopUpPackageDto> Items
);