using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Source-gen companion for <c>[ZigZag]</c>.
/// Emits calls to the ZigZag reader/writer methods for signed 32-bit integers.
/// </summary>
public sealed class ZigZagCodeGen : ISerializerRuleCodeGen
{
    public bool CanHandle(SourceGenPropertyContext ctx)
        => ctx.HasAttribute("ZigZagAttribute") && IsSupported(ctx.UnderlyingType);

    public string EmitReadExpression(SourceGenPropertyContext ctx)
    {
        var readExpr = "reader.ReadZigZagInt32()";

        if (ctx.IsNullableValueType)
            return $"reader.IsAtEnd() ? (int?)null : {readExpr}";

        return readExpr;
    }

    public string EmitWriteStatement(SourceGenPropertyContext ctx, string valueExpression)
    {
        if (ctx.IsNullableValueType)
        {
            return $"if ({valueExpression} != null)\n" +
                   "{\n" +
                   $"    writer.WriteZigZagInt32({valueExpression}.Value);\n" +
                   "}";
        }

        return $"writer.WriteZigZagInt32({valueExpression});";
    }

    public int? SerializedSize(SourceGenPropertyContext ctx) => null; // variable-length

    private static bool IsSupported(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Int32;
}
