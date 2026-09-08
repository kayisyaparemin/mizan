using CoinFlow.Application.Models;

namespace CoinFlow.App.Models;

/// <summary>
/// Listede duran kaydedilmiş geçici plan. Koşulların kendisini de taşır;
/// "Yükle" ayrıca veritabanına gitmez.
/// </summary>
public sealed record SavedSimulationDraftView(
    Guid Id,
    string Name,
    string SummaryText,
    IReadOnlyList<SimulationDraftCondition> Conditions);
