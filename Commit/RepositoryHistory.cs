using System.IO;
using System.Text.Json;

namespace Commit;

internal sealed record RepositoryHistory(string LastPath, string[] Paths)
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommitPush");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "repository-history.json");

    public static RepositoryHistory Empty { get; } = new(string.Empty, []);

    public static RepositoryHistory Load()
    {
        try
        {
            return JsonSerializer.Deserialize<RepositoryHistory>(File.ReadAllText(FilePath)) ?? Empty;
        }
        catch (FileNotFoundException)
        {
            return Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return Empty;
        }
    }

    public RepositoryHistory WithRepository(string root)
    {
        string[] paths = Paths.Prepend(root)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        return this with { Paths = paths };
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporaryFile = Path.Combine(DirectoryPath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryFile, JsonSerializer.Serialize(this));
            File.Move(temporaryFile, FilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryFile);
        }
    }

    public static void Clear()
    {
        if (Directory.Exists(DirectoryPath))
            File.Delete(FilePath);
    }
}