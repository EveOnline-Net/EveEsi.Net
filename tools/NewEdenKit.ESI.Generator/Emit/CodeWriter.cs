using System.Text;

namespace NewEdenKit.ESI.Generator.Emit;

/// <summary>Builds C# source with four-space indentation and LF line endings, so output is byte-for-byte stable.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    public CodeWriter Line(string text = "")
    {
        if (text.Length > 0)
        {
            _builder.Append(' ', _indent * 4);
        }

        _builder.Append(text).Append('\n');
        return this;
    }

    public CodeWriter Open(string header)
    {
        Line(header).Line("{");
        _indent++;
        return this;
    }

    public CodeWriter Close(string suffix = "")
    {
        _indent--;
        return Line("}" + suffix);
    }

    /// <summary>A one-line XML doc summary; the text is escaped.</summary>
    public CodeWriter Summary(string text) => Line($"/// <summary>{Xml(text)}</summary>");

    public static string Xml(string text) =>
        text.ReplaceLineEndings(" ").Trim().Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>A C# string literal.</summary>
    public static string Literal(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    public override string ToString() => _builder.ToString();
}
