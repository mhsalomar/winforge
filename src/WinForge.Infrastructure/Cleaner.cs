using WinForge.Core;

namespace WinForge.Infrastructure;

public sealed record CleanerCategory(string Id, IReadOnlyList<string> Roots, bool SelectedByDefault, string Explanation, TimeSpan MinimumAge = default);

public sealed class SafeCleaner : ICleaner
{
    private readonly IReadOnlyDictionary<string, CleanerCategory> _categories;

    public SafeCleaner(IReadOnlyCollection<CleanerCategory>? categories = null)
    {
        _categories = (categories ?? DefaultCategories()).ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<CleanerCategory> Categories => _categories.Values.ToArray();

    public async Task<IReadOnlyList<CleanerPreview>> PreviewAsync(IReadOnlyCollection<string> categories, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => categories.Select(id => PreviewOne(id, cancellationToken)).ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CleanerResult>> CleanAsync(IReadOnlyCollection<CleanerPreview> previews, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => previews.Select(x => CleanOne(x, cancellationToken)).ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public bool IsPathAllowed(string category, string path)
    {
        if (!_categories.TryGetValue(category, out var definition)) return false;
        var fullPath = Path.GetFullPath(path);
        return definition.Roots.Any(root => IsUnderRoot(fullPath, Path.GetFullPath(root))) && !HasReparsePoint(fullPath, definition.Roots);
    }

    private CleanerPreview PreviewOne(string id, CancellationToken cancellationToken)
    {
        if (!_categories.TryGetValue(id, out var category)) return new(id, 0, 0, [], ["Unknown category."]);
        var items = new List<CleanerItem>();
        var errors = new List<string>();
        foreach (var root in category.Roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            EnumerateSafe(root, root, id, DateTime.UtcNow - category.MinimumAge, items, errors, cancellationToken);
        }
        return new(id, items.Count, items.Sum(x => x.Length), items, errors);
    }

    private CleanerResult CleanOne(CleanerPreview preview, CancellationToken cancellationToken)
    {
        var deleted = 0;
        long bytes = 0;
        var errors = new List<string>();
        foreach (var item in preview.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPathAllowed(preview.Category, item.Path)) { errors.Add($"Blocked unsafe path: {item.Path}"); continue; }
            try { File.Delete(item.Path); deleted++; bytes += item.Length; }
            catch (IOException ex) { errors.Add(ex.Message); }
            catch (UnauthorizedAccessException ex) { errors.Add(ex.Message); }
        }
        return new(preview.Category, deleted, bytes, errors);
    }

    private static void EnumerateSafe(string root, string current, string category, DateTime oldestAllowedWriteTime, List<CleanerItem> items, List<string> errors, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var info = new DirectoryInfo(current);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return;
            foreach (var file in info.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && file.LastWriteTimeUtc <= oldestAllowedWriteTime) items.Add(new(file.FullName, file.Length, category));
            }
            foreach (var directory in info.EnumerateDirectories()) EnumerateSafe(root, directory.FullName, category, oldestAllowedWriteTime, items, errors, cancellationToken);
        }
        catch (UnauthorizedAccessException ex) { errors.Add(ex.Message); }
        catch (IOException ex) { errors.Add(ex.Message); }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReparsePoint(string path, IReadOnlyList<string> roots)
    {
        var root = roots.Select(Path.GetFullPath).Where(x => IsUnderRoot(path, x)).OrderByDescending(x => x.Length).FirstOrDefault();
        if (root is null) return true;
        var current = new FileInfo(path).Directory;
        while (current is not null && IsUnderRoot(current.FullName + Path.DirectorySeparatorChar, root))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            current = current.Parent;
        }
        return false;
    }

    private static IReadOnlyCollection<CleanerCategory> DefaultCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var firefoxProfiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        var firefoxCaches = Directory.Exists(firefoxProfiles)
            ? Directory.EnumerateDirectories(firefoxProfiles).Select(x => Path.Combine(x, "cache2")).Where(Directory.Exists).ToArray()
            : [];
        return
        [
            new("user-temp", [Path.GetTempPath()], true, "Per-user temporary files older than 48 hours.", TimeSpan.FromHours(48)),
            new("windows-temp", [Path.Combine(windows, "Temp")], false, "System temporary files older than 48 hours; administrator access may be required.", TimeSpan.FromHours(48)),
            new("wer", [Path.Combine(local, "Microsoft", "Windows", "WER")], true, "Windows Error Reporting archives and queues older than 24 hours.", TimeSpan.FromHours(24)),
            new("directx-shader", [Path.Combine(local, "D3DSCache")], false, "DirectX shader cache; rebuilt as needed."),
            new("thumbnails", [Path.Combine(local, "Microsoft", "Windows", "Explorer")], false, "Thumbnail cache; rebuilt by Explorer."),
            new("nvidia-shader", [Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "NVIDIA", "GLCache")], false, "NVIDIA shader caches."),
            new("amd-shader", [Path.Combine(local, "AMD", "DxCache"), Path.Combine(local, "AMD", "GLCache")], false, "AMD shader caches."),
            new("steam-web", [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "config", "htmlcache")], false, "Steam web UI cache only."),
            new("epic-web", [Path.Combine(local, "EpicGamesLauncher", "Saved", "webcache")], false, "Epic Games Launcher web cache only."),
            new("battlenet-web", [Path.Combine(local, "Battle.net", "Cache")], false, "Battle.net launcher cache only."),
            new("browser-edge", [Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache")], false, "Edge cache; cookies and profiles are excluded."),
            new("browser-chrome", [Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache")], false, "Chrome cache; cookies and profiles are excluded."),
            new("browser-brave", [Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache")], false, "Brave cache; cookies and profiles are excluded."),
            new("browser-firefox", firefoxCaches, false, "Firefox cache2 folders only; cookies and profiles are excluded."),
            new("user-crash-dumps", [Path.Combine(local, "CrashDumps")], false, "Per-user crash dumps."),
            new("minidumps", [Path.Combine(windows, "Minidump")], false, "Windows minidumps; useful for diagnostics."),
            new("adobe-cache", [Path.Combine(roaming, "Adobe", "Common", "Media Cache Files")], false, "Adobe media cache only.")
        ];
    }
}
