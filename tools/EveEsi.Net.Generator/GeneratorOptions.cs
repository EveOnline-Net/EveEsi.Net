namespace EveEsi.Net.Generator;

/// <summary>Command-line options for the generator.</summary>
internal sealed record GeneratorOptions(string SpecPath, string OutputPath, string ConfigPath)
{
    public const string Usage = "Usage: EveEsi.Net.Generator --spec <openapi.json> --out <src dir> [--config <generator.json>]";

    /// <summary>Parses the arguments, or returns <see langword="null"/> with an error message.</summary>
    public static GeneratorOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        string? spec = null, output = null, config = null;

        for (var i = 0; i < args.Count; i++)
        {
            var value = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i])
            {
                case "--spec": spec = value; i++; break;
                case "--out": output = value; i++; break;
                case "--config": config = value; i++; break;
                default:
                    error = $"Unknown argument '{args[i]}'.";
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(spec) || string.IsNullOrWhiteSpace(output))
        {
            error = "Both --spec and --out are required.";
            return null;
        }

        // generator.json sits next to the spec by default, so the documented command needs no extra flag.
        config ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(spec)) ?? ".", "generator.json");

        error = null;
        return new GeneratorOptions(spec, output, config);
    }
}
