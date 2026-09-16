using CoinFlow.Application.Services;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Models;

public sealed record ProjectionQueryPlan(
    FinancialPlan Plan,
    ProjectionBoundary? Boundary);
