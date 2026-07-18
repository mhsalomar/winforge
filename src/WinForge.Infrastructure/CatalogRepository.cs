using System.Text.Json;
using WinForge.Core;

namespace WinForge.Infrastructure;

public sealed record LoadedCatalog(CatalogDocument Catalog, string Hash, string Path);

public sealed class CatalogRepository
{
    private readonly CatalogEngine _engine = new();

    public async Task<LoadedCatalog> LoadAsync(string path, IReadOnlySet<string>? localizationKeys = null, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Catalog file was not found.", fullPath);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var catalog = JsonSerializer.Deserialize<CatalogDocument>(bytes, JsonDefaults.Options) ?? throw new InvalidDataException("Catalog is empty or invalid.");
        var issues = _engine.Validate(catalog, localizationKeys);
        if (issues.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, issues.Select(x => $"{x.Code}: {x.Message}")));
        return new(catalog, CatalogEngine.ComputeHash(bytes), fullPath);
    }
}
