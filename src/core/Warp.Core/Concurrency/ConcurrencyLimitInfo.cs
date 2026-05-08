namespace Warp.Core.Concurrency;

public sealed record ConcurrencyLimitInfo(string Name, int Limit, DateTime UpdatedAt);
