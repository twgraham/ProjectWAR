using System.Text;

namespace RpcSourceGenerator;

/// <summary>
/// A thin wrapper around <see cref="StringBuilder"/> that tracks indentation level,
/// eliminating manual space-padding from every <c>AppendLine</c> call in the source generator.
/// </summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private const string IndentUnit = "    "; // 4 spaces per level

    /// <summary>Increments the indent level by one.</summary>
    public void Indent() => _indent++;

    /// <summary>Decrements the indent level by one.</summary>
    public void Outdent() => _indent--;

    /// <summary>Writes an indented line followed by a newline.</summary>
    public void AppendLine(string line)
    {
        WriteIndent();
        _sb.AppendLine(line);
    }

    /// <summary>Writes an empty line (no indentation).</summary>
    public void AppendLine() => _sb.AppendLine();

    /// <summary>
    /// Writes <c>"{"</c> at the current indent level and increments the indent.
    /// Optionally writes a header line before the opening brace.
    /// </summary>
    public void OpenBlock(string? header = null)
    {
        if (header != null) AppendLine(header);
        AppendLine("{");
        _indent++;
    }

    /// <summary>Decrements the indent and writes <c>"}"</c>.</summary>
    public void CloseBlock()
    {
        _indent--;
        AppendLine("}");
    }

    /// <summary>
    /// Writes multi-line content produced by an <see cref="Rules.ISerializerRuleCodeGen"/>.
    /// Each non-blank line is written at the current indent level, preserving any
    /// relative indentation embedded within the content.
    /// </summary>
    public void AppendMultiLine(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(trimmed))
                AppendLine(trimmed);
        }
    }

    /// <summary>Returns the accumulated source text.</summary>
    public override string ToString() => _sb.ToString();

    private void WriteIndent()
    {
        for (int i = 0; i < _indent; i++)
            _sb.Append(IndentUnit);
    }
}
