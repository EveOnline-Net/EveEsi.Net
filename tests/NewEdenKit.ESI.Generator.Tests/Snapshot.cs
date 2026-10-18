using System.Reflection;
using NewEdenKit.ESI.Generator.Emit;

namespace NewEdenKit.ESI.Generator.Tests;

/// <summary>
/// Compares generated files with the committed snapshots in <c>Snapshots/</c>. Set <c>UPDATE_SNAPSHOTS=1</c> to
/// rewrite them, then review the diff like any other change.
/// </summary>
internal static class Snapshot
{
    public static string TestDirectory() =>
        typeof(Snapshot).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ProjectDirectory").Value!;

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(TestDirectory(), "Fixtures", name));

    /// <summary>Matches the generated files under <paramref name="outputDirectory"/> against <c>Snapshots/&lt;snapshotDirectory&gt;</c>.</summary>
    public static void Match(string snapshotDirectory, IReadOnlyList<GeneratedFile> allFiles, string outputDirectory)
    {
        var root = Path.Combine(TestDirectory(), "Snapshots", snapshotDirectory);
        var update = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";
        var files = allFiles
            .Where(f => f.RelativePath.StartsWith(outputDirectory + "/", StringComparison.Ordinal))
            .Select(f => f with { RelativePath = f.RelativePath[(outputDirectory.Length + 1)..] })
            .ToList();

        foreach (var file in files)
        {
            var path = Path.Combine(root, file.RelativePath);
            if (update)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Content);
                continue;
            }

            File.Exists(path).ShouldBeTrue($"Missing snapshot {path}. Run with UPDATE_SNAPSHOTS=1.");
            file.Content.ShouldBe(File.ReadAllText(path).ReplaceLineEndings("\n"), $"Snapshot {file.RelativePath} differs.");
        }

        if (!update)
        {
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal)
                .ShouldBe(files.Select(f => f.RelativePath).Order(StringComparer.Ordinal), "Snapshot files don't match generated files.");
        }
    }
}
