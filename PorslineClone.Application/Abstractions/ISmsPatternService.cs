namespace PorslineClone.Application.Abstractions;

public interface ISmsPatternService
{
    Task<IReadOnlyList<Application.Contracts.SmsPatternCategoryDto>> GetGroupedAsync(CancellationToken ct = default);
    Task<string> RenderAsync(string key, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);
    Task EnsureSeededAsync(CancellationToken ct = default);
    Task ResetToDefaultAsync(string key, CancellationToken ct = default);
    Task UpdateTemplatesAsync(IReadOnlyList<Application.Contracts.UpdateSmsPatternItem> items, CancellationToken ct = default);
}
