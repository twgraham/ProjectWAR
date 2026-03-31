using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Source-gen rule for enum properties.
/// Emits <c>(EnumType)reader.ReadByte()</c> / <c>writer.WriteByte((byte)value)</c>.
/// </summary>
public sealed class EnumCodeGen : ISerializerRuleCodeGen
{
    public bool CanHandle(SourceGenPropertyContext ctx)
        => ctx.UnderlyingType.TypeKind == TypeKind.Enum;

    public string EmitReadExpression(SourceGenPropertyContext ctx)
    {
        var enumTypeName = ctx.UnderlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return ctx.IsNullableValueType
            ? $"reader.IsAtEnd() ? ({enumTypeName}?)null : ({enumTypeName})reader.ReadByte()"
            : $"({enumTypeName})reader.ReadByte()";
    }

    public string EmitWriteStatement(SourceGenPropertyContext ctx, string valueExpression)
    {
        if (ctx.IsNullableValueType)
        {
            return $"if ({valueExpression} != null)\n" +
                   "{\n" +
                   $"    writer.WriteByte((byte){valueExpression}.Value);\n" +
                   "}";
        }

        return $"writer.WriteByte((byte){valueExpression});";
    }

    public int? SerializedSize(SourceGenPropertyContext ctx) => 1;
}
