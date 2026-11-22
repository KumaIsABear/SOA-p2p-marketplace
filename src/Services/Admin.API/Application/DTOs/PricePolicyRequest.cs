using System.ComponentModel.DataAnnotations;

namespace Admin.API.Application.DTOs;

public sealed record PricePolicyRequest(
    [param: Required] long? BasePrice,
    [param: Required] int? BandBasisPoints,
    [param: Required] long? ExpectedVersion,
    [param: Required, MaxLength(500)] string Reason
);
