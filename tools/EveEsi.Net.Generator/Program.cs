using System.Text.Json;
using EveEsi.Net.Generator;

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

using var spec = JsonDocument.Parse(File.ReadAllBytes(options.SpecPath));
var root = spec.RootElement;
var compatibilityDate = root.GetProperty("components").GetProperty("parameters")
    .GetProperty("CompatibilityDate").GetProperty("schema").GetProperty("enum")[0].GetString();

Console.WriteLine($"Spec: {options.SpecPath} (OpenAPI {root.GetProperty("openapi").GetString()}, compatibility date {compatibilityDate})");
Console.WriteLine($"Config: {options.ConfigPath}");
Console.WriteLine($"Output: {options.OutputPath}");

// Fail loudly rather than silently produce nothing: callers must not mistake this for a successful run.
Console.Error.WriteLine("Code generation is not implemented yet.");
return 2;
