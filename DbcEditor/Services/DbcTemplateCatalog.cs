using System.IO;

namespace DbcEditor.Services;

public sealed class DbcTemplateInfo
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }

    public override string ToString() => Name;
}

public static class DbcTemplateCatalog
{
    public static IReadOnlyList<DbcTemplateInfo> Discover()
    {
        var byName = new Dictionary<string, DbcTemplateInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in CandidateDirectories())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*.dbc"))
            {
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name) || byName.ContainsKey(name)) continue;
                byName[name] = new DbcTemplateInfo { Name = name, FullPath = path };
            }
        }

        return byName.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Templates");

        // Prefer project Templates when running from bin (dev)
        var up = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Templates"));
        if (Directory.Exists(up)) yield return up;

        var publicDocs = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        yield return Path.Combine(publicDocs, "Vector", "CANdb++ 3.1", "Templates");
        yield return Path.Combine(publicDocs, "Vector", "CANoe", "12.0 (x64)", "Templates", "Database");

        yield return @"C:\Program Files (x86)\Vector CANdb++ Admin 3.0 SP27\Exec32\Templates";
    }
}
