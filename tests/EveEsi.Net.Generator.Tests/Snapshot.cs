using System.Runtime.CompilerServices;
using EveEsi.Net.Generator.Emit;

namespace EveEsi.Net.Generator.Tests;

/// <summary>
/// Compares generated files with the committed snapshots in <c>Snapshots/</c>. Set <c>UPDATE_SNAPSHOTS=1</c> to
/// rewrite them, then review the diff like any other change.
/// </summary>
internal static class Snapshot
{
    public static string TestDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(TestDirectory(), "Fixtures", name));

    public static void Match(string snapshotDirectory, IReadOnlyList<GeneratedFile> files)
    {
        var root = Path.Combine(TestDirectory(), "Snapshots", snapshotDirectory);
        var update = Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";

        foreach (var file in files)
        {
            var path = Path.Combine(root, Path.GetFileName(file.RelativePath));
            if (update)
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(path, file.Content);
                continue;
            }

            File.Exists(path).ShouldBeTrue($"Missing snapshot {path}. Run with UPDATE_SNAPSHOTS=1.");
            file.Content.ShouldBe(File.ReadAllText(path).ReplaceLineEndings("\n"), $"Snapshot {file.RelativePath} differs.");
        }

        if (!update)
        {
            Directory.GetFiles(root).Select(Path.GetFileName).Order()
                .ShouldBe(files.Select(f => Path.GetFileName(f.RelativePath)).Order(), "Snapshot files don't match generated files.");
        }
    }
}
