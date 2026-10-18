using System.Text;
using EveEsi.Net.Generator;
using EveEsi.Net.Generator.Config;
using EveEsi.Net.Generator.Spec;

var options = GeneratorOptions.Parse(args, out var error);
if (options is null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(GeneratorOptions.Usage);
    return 1;
}

foreach (var path in new[] { options.SpecPath, options.ConfigPath })
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"File not found: {path}");
        return 1;
    }
}

GenerationResult result;
try
{
    result = CodeGenerator.Generate(OpenApiSpec.Load(options.SpecPath), GeneratorConfig.Load(options.ConfigPath));
}
catch (GenerationException ex)
{
    Console.Error.WriteLine($"Generation failed: {ex.Message}");
    return 2;
}

var outputRoot = Path.GetFullPath(options.OutputPath);
var written = result.Files.Select(f => Path.GetFullPath(Path.Combine(outputRoot, f.RelativePath))).ToHashSet(StringComparer.OrdinalIgnoreCase);

// Remove generated files that no longer correspond to anything in the spec.
foreach (var directory in CodeGenerator.OwnedDirectories.Select(d => Path.Combine(outputRoot, d)).Where(Directory.Exists))
{
    foreach (var stale in Directory.EnumerateFiles(directory, "*.g.cs", SearchOption.AllDirectories).Where(f => !written.Contains(Path.GetFullPath(f))))
    {
        File.Delete(stale);
        Console.WriteLine($"Deleted {Path.GetRelativePath(outputRoot, stale)}");
    }

    // Deepest first, so a folder emptied by the deletes above goes too.
    foreach (var empty in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                 .OrderByDescending(d => d.Length)
                 .Where(d => !Directory.EnumerateFileSystemEntries(d).Any()))
    {
        Directory.Delete(empty);
    }
}

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
foreach (var file in result.Files)
{
    var path = Path.Combine(outputRoot, file.RelativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, file.Content, utf8);
}

Console.WriteLine($"Wrote {result.Files.Count} files to {outputRoot}");
foreach (var note in result.Notes)
{
    Console.WriteLine($"Note: {note}");
}

return 0;
