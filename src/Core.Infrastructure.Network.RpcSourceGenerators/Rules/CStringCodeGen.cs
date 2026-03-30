using System.Linq;
using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Source-gen companion for <c>[CString]</c>.
/// Emits direct calls to <c>reader.ReadCString()</c>/<c>writer.WriteCString()</c>.
/// </summary>
public sealed class CStringCodeGen : ISerializerRuleCodeGen
{
    public bool CanHandle(SourceGenPropertyContext ctx)
        => ctx.HasAttribute("CStringAttribute") &&
           ctx.UnderlyingType.SpecialType == SpecialType.System_String;

    public string EmitReadExpression(SourceGenPropertyContext ctx)
    {
        var length = GetLength(ctx);
        var inner = length.HasValue
            ? $"reader.ReadCString({length.Value})"
            : "reader.ReadCString(null)";
        return ctx.IsNullable
            ? $"reader.IsAtEnd() ? null : {inner}"
            : inner;
    }

    public string EmitWriteStatement(SourceGenPropertyContext ctx, string valueExpression)
    {
        var length = GetLength(ctx);
        return length.HasValue
            ? $"writer.WriteCString({valueExpression}, {length.Value});"
            : $"writer.WriteCString({valueExpression}, null);";
    }

    public int? SerializedSize(SourceGenPropertyContext ctx) => GetLength(ctx);

    private static int? GetLength(SourceGenPropertyContext ctx)
    {
        var attr = ctx.GetAttribute("CStringAttribute");
        if (attr != null && attr.ConstructorArguments.Length > 0 &&
            attr.ConstructorArguments[0].Value is int len)
            return len;
        return null;
    }
}
