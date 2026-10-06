using AWM.Models;

namespace AWM.Services.Interfaces;

public interface IDraftService
{
    Task<BlogDraft> CreateAsync(DraftRequest request, CancellationToken ct);
}
