using Mizan.Application.Services;
using Mizan.Domain.Models;

namespace Mizan.Application.Models;

public sealed record ProjectionQueryPlan(
    FinancialPlan Plan,
    ProjectionBoundary? Boundary);
