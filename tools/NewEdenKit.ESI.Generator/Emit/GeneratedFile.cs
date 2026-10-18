namespace NewEdenKit.ESI.Generator.Emit;

/// <summary>One generated file, its path relative to the <c>--out</c> directory (forward slashes).</summary>
internal sealed record GeneratedFile(string RelativePath, string Content);
