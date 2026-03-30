using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Source-gen companion for <c>[PascalString]</c>.
/// Emits direct calls to <c>reader.ReadPascalString()</c>/<c>writer.WritePascalString()</c>
/// without any runtime reflection.
/// </summary>
public sealed class PascalStringCodeGen : ISerializerRuleCodeGen
{
    public bool CanHandle(SourceGenPropertyContext ctx)
        => ctx.HasAttribute("PascalStringAttribute") &&
           ctx.UnderlyingType.SpecialType == SpecialType.System_String;

    public string EmitReadExpression(SourceGenPropertyContext ctx)
        => ctx.IsNullable
            ? "reader.IsAtEnd() ? null : reader.ReadPascalString()"
            : "reader.ReadPascalString()";

    public string EmitWriteStatement(SourceGenPropertyContext ctx, string valueExpression)
        => $"writer.WritePascalString({valueExpression});";

    public int? SerializedSize(SourceGenPropertyContext ctx) => null; // variable-length
}
