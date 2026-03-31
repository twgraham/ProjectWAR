using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Source-gen companion for <c>[LittleEndian]</c>.
/// Emits calls to the LE reader/writer variants for numeric primitives.
/// </summary>
public sealed class LittleEndianCodeGen : ISerializerRuleCodeGen
{
    public bool CanHandle(SourceGenPropertyContext ctx)
        => ctx.HasAttribute("LittleEndianAttribute") && IsNumeric(ctx.UnderlyingType);

    public string EmitReadExpression(SourceGenPropertyContext ctx)
    {
        var readExpr = ctx.UnderlyingType.SpecialType switch
        {
            SpecialType.System_Int16 => "reader.ReadInt16LE()",
            SpecialType.System_UInt16 => "reader.ReadUInt16LE()",
            SpecialType.System_Int32 => "reader.ReadInt32LE()",
            SpecialType.System_UInt32 => "reader.ReadUInt32LE()",
            SpecialType.System_Int64 => "reader.ReadInt64LE()",
            SpecialType.System_UInt64 => "reader.ReadUInt64LE()",
            SpecialType.System_Single => "reader.ReadFloatLE()",
            SpecialType.System_Double => "reader.ReadDoubleLE()",
            _ => "null /* unsupported [LittleEndian] type */"
        };

        if (ctx.IsNullableValueType)
        {
            var typeName = ctx.UnderlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"reader.IsAtEnd() ? ({typeName}?)null : {readExpr}";
        }

        return readExpr;
    }

    public string EmitWriteStatement(SourceGenPropertyContext ctx, string valueExpression)
    {
        var writeMethod = ctx.UnderlyingType.SpecialType switch
        {
            SpecialType.System_Int16 => "WriteInt16LE",
            SpecialType.System_UInt16 => "WriteUInt16LE",
            SpecialType.System_Int32 => "WriteInt32LE",
            SpecialType.System_UInt32 => "WriteUInt32LE",
            SpecialType.System_Int64 => "WriteInt64LE",
            SpecialType.System_UInt64 => "WriteUInt64LE",
            SpecialType.System_Single => "WriteFloatLE",
            SpecialType.System_Double => "WriteDoubleLE",
            _ => null
        };

        if (writeMethod == null)
            return "/* unsupported [LittleEndian] type */";

        if (ctx.IsNullableValueType)
        {
            return $"if ({valueExpression} != null)\n" +
                   "{\n" +
                   $"    writer.{writeMethod}({valueExpression}.Value);\n" +
                   "}";
        }

        return $"writer.{writeMethod}({valueExpression});";
    }

    public int? SerializedSize(SourceGenPropertyContext ctx)
    {
        return ctx.UnderlyingType.SpecialType switch
        {
            SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,
            _ => null
        };
    }

    private static bool IsNumeric(ITypeSymbol type)
    {
        return type.SpecialType is
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double;
    }
}
