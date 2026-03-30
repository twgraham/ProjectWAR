using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using RpcSourceGenerator.Rules;

namespace RpcSourceGenerator;

/// <summary>
/// Shared utility methods for source-generated serialization code emission.
/// Extracted from <see cref="PacketSerializerGenerator"/> so that rule implementations
/// (e.g. <see cref="CollectionCodeGen"/>) and the generator itself can share
/// the same emit helpers without coupling to the generator class.
/// </summary>
internal static class SerializerCodeGenUtilities
{
    // ------------------------------------------------------------------
    //  Type classification
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if the type is a custom class/struct that needs its own
    /// Deserialize/Serialize method pair (i.e. not a primitive, enum, string, array, or collection).
    /// </summary>
    internal static bool ShouldGenerateSerializerFor(ITypeSymbol type, out INamedTypeSymbol typeToAdd)
    {
        typeToAdd = null!;

        // Skip primitive types and special types
        if (type.SpecialType != SpecialType.None)
            return false;

        // Skip strings
        if (type.SpecialType == SpecialType.System_String)
            return false;

        // Skip enums
        if (type.TypeKind == TypeKind.Enum)
            return false;

        // Skip arrays
        if (type is IArrayTypeSymbol)
            return false;

        // Skip collections
        if (IsCollectionType(type, out _))
            return false;

        // Skip object type
        if (type.SpecialType == SpecialType.System_Object)
            return false;

        // Only consider named types (classes/structs)
        if (type is INamedTypeSymbol namedType)
        {
            // Skip generic type definitions
            if (namedType.IsUnboundGenericType)
                return false;

            typeToAdd = namedType;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the type is a supported collection (array, <c>List&lt;T&gt;</c>,
    /// <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>),
    /// excluding <c>byte[]</c> which is handled specially and <c>IEnumerable&lt;T&gt;</c>
    /// which has no <c>Count</c> property and cannot be efficiently serialized.
    /// </summary>
    internal static bool IsCollectionType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;

        switch (type)
        {
            // Check for arrays (except byte[] which is handled specially)
            case IArrayTypeSymbol arrayType:
                elementType = arrayType.ElementType;
                return elementType.SpecialType != SpecialType.System_Byte;
            // Check for generic collections
            case INamedTypeSymbol { IsGenericType: true } namedType:
            {
                var genericDef = namedType.ConstructedFrom;
                var genericDefString = genericDef.ToDisplayString();

                if (genericDefString is "System.Collections.Generic.List<T>"
                    or "System.Collections.Generic.IList<T>"
                    or "System.Collections.Generic.ICollection<T>")
                {
                    elementType = namedType.TypeArguments[0];
                    return true;
                }

                break;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    //  Code emission — type → read/write expression
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns a C# expression that reads a value of the given type from a
    /// <c>BinaryPacketSerializer.SpanReader</c> named <c>reader</c>.
    /// Handles custom types, enums, primitives (with optional LE), booleans, and strings.
    /// </summary>
    internal static string EmitReadForType(ITypeSymbol type, bool littleEndian = false)
    {
        // Custom reference types
        if (ShouldGenerateSerializerFor(type, out var customType))
            return $"Deserialize{GetSafeTypeName(customType)}(ref reader)";

        // Nested collections
        if (IsCollectionType(type, out _))
            throw new NotSupportedException("Nested collections are not yet supported");

        // Enums
        if (type.TypeKind == TypeKind.Enum)
        {
            var enumTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({enumTypeName})reader.ReadByte()";
        }

        // Primitives, booleans, strings
        return type.SpecialType switch
        {
            SpecialType.System_Byte => "reader.ReadByte()",
            SpecialType.System_SByte => "reader.ReadSByte()",
            SpecialType.System_Int16 => littleEndian ? "reader.ReadInt16LE()" : "reader.ReadInt16()",
            SpecialType.System_UInt16 => littleEndian ? "reader.ReadUInt16LE()" : "reader.ReadUInt16()",
            SpecialType.System_Int32 => littleEndian ? "reader.ReadInt32LE()" : "reader.ReadInt32()",
            SpecialType.System_UInt32 => littleEndian ? "reader.ReadUInt32LE()" : "reader.ReadUInt32()",
            SpecialType.System_Int64 => littleEndian ? "reader.ReadInt64LE()" : "reader.ReadInt64()",
            SpecialType.System_UInt64 => littleEndian ? "reader.ReadUInt64LE()" : "reader.ReadUInt64()",
            SpecialType.System_Single => littleEndian ? "reader.ReadFloatLE()" : "reader.ReadFloat()",
            SpecialType.System_Double => littleEndian ? "reader.ReadDoubleLE()" : "reader.ReadDouble()",
            SpecialType.System_Boolean => "(reader.ReadByte() != 0)",
            SpecialType.System_String => "reader.ReadString()",
            _ => "null /* unsupported type */"
        };
    }

    /// <summary>
    /// Returns a C# expression that writes a value of the given type to a
    /// <c>BinaryPacketSerializer.SpanWriter</c> named <c>writer</c>.
    /// Does NOT include a trailing semicolon.
    /// Handles custom types, enums, primitives (with optional LE), booleans, and strings.
    /// </summary>
    internal static string EmitWriteForType(ITypeSymbol type, string valueExpression, bool littleEndian = false)
    {
        // Custom reference types
        if (ShouldGenerateSerializerFor(type, out var customType))
            return $"Serialize{GetSafeTypeName(customType)}({valueExpression}, ref writer)";

        // Nested collections
        if (IsCollectionType(type, out _))
            throw new NotSupportedException("Nested collections are not yet supported");

        // Enums
        if (type.TypeKind == TypeKind.Enum)
            return $"writer.WriteByte((byte){valueExpression})";

        // Primitives, booleans, strings
        return type.SpecialType switch
        {
            SpecialType.System_Byte => $"writer.WriteByte({valueExpression})",
            SpecialType.System_SByte => $"writer.WriteSByte({valueExpression})",
            SpecialType.System_Int16 => littleEndian ? $"writer.WriteInt16LE({valueExpression})" : $"writer.WriteInt16({valueExpression})",
            SpecialType.System_UInt16 => littleEndian ? $"writer.WriteUInt16LE({valueExpression})" : $"writer.WriteUInt16({valueExpression})",
            SpecialType.System_Int32 => littleEndian ? $"writer.WriteInt32LE({valueExpression})" : $"writer.WriteInt32({valueExpression})",
            SpecialType.System_UInt32 => littleEndian ? $"writer.WriteUInt32LE({valueExpression})" : $"writer.WriteUInt32({valueExpression})",
            SpecialType.System_Int64 => littleEndian ? $"writer.WriteInt64LE({valueExpression})" : $"writer.WriteInt64({valueExpression})",
            SpecialType.System_UInt64 => littleEndian ? $"writer.WriteUInt64LE({valueExpression})" : $"writer.WriteUInt64({valueExpression})",
            SpecialType.System_Single => littleEndian ? $"writer.WriteFloatLE({valueExpression})" : $"writer.WriteFloat({valueExpression})",
            SpecialType.System_Double => littleEndian ? $"writer.WriteDoubleLE({valueExpression})" : $"writer.WriteDouble({valueExpression})",
            SpecialType.System_Boolean => $"writer.WriteByte((byte)({valueExpression} ? 1 : 0))",
            SpecialType.System_String => $"writer.WriteString({valueExpression})",
            _ => "/* unsupported type */"
        };
    }

    // ------------------------------------------------------------------
    //  Wire size computation
    // ------------------------------------------------------------------

    /// <summary>
    /// Attempts to compute the fixed wire size (in bytes) of a type.
    /// Returns null if the type contains variable-length fields (strings, collections, etc.).
    /// </summary>
    internal static int? TryComputeWireSize(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Boolean)
            return 1;

        if (type.TypeKind == TypeKind.Enum)
            return 1;

        switch (type.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
                return 1;
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
                return 2;
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single:
                return 4;
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Double:
                return 8;
        }

        // Named type (class/struct) — sum of property wire sizes
        if (type is INamedTypeSymbol namedType && type.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            var props = namedType.GetMembers().OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                .ToList();

            int total = 0;
            foreach (var prop in props)
            {
                var size = TryComputePropertyWireSize(prop);
                if (size == null) return null;
                total += size.Value;
            }
            return total;
        }

        return null;
    }

    /// <summary>
    /// Computes the fixed wire size of a single property, dispatching to the rule registry first.
    /// </summary>
    internal static int? TryComputePropertyWireSize(IPropertySymbol prop)
    {
        // Check ISerializerRuleCodeGen registry first
        var ruleCtx = new SourceGenPropertyContext(prop);
        var rule = SerializerCodeGenRuleRegistry.Resolve(ruleCtx);
        if (rule != null)
            return rule.SerializedSize(ruleCtx);

        var propType = prop.Type;
        var underlyingType = propType;
        if (propType.NullableAnnotation == NullableAnnotation.Annotated
            && propType is INamedTypeSymbol namedNullable
            && namedNullable.IsGenericType
            && namedNullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
            underlyingType = namedNullable.TypeArguments[0];

        // Custom type — recurse
        if (underlyingType is INamedTypeSymbol customType && ShouldGenerateSerializerFor(underlyingType, out _))
            return TryComputeWireSize(customType);

        // Primitive / boolean
        return TryComputeWireSize(underlyingType);
    }

    // ------------------------------------------------------------------
    //  Collection code emission helpers
    // ------------------------------------------------------------------

    internal static void GenerateEntrySizeWrite(CodeWriter w, int value, int byteWidth, bool littleEndian)
    {
        switch (byteWidth)
        {
            case 1:
                w.AppendLine($"writer.WriteByte({value});");
                break;
            case 2:
                w.AppendLine(littleEndian
                    ? $"writer.WriteUInt16LE({value});"
                    : $"writer.WriteUInt16({value});");
                break;
            case 4:
                w.AppendLine(littleEndian
                    ? $"writer.WriteUInt32LE({(uint)value});"
                    : $"writer.WriteUInt32({(uint)value});");
                break;
        }
    }

    internal static string GenerateEntrySizeRead(int byteWidth, bool littleEndian)
    {
        return byteWidth switch
        {
            1 => "reader.ReadByte()",
            2 => littleEndian ? "reader.ReadUInt16LE()" : "reader.ReadUInt16()",
            4 => littleEndian ? "reader.ReadUInt32LE()" : "reader.ReadUInt32()",
            _ => throw new InvalidOperationException($"Invalid sized entry width: {byteWidth}")
        };
    }

    internal static string GenerateLengthRead(int lengthSize, bool littleEndian = false)
    {
        return lengthSize switch
        {
            1 => "reader.ReadByte()",
            2 => littleEndian ? "reader.ReadUInt16LE()" : "reader.ReadUInt16()",
            4 => littleEndian ? "reader.ReadUInt32LE()" : "reader.ReadUInt32()",
            _ => throw new InvalidOperationException($"Invalid length size: {lengthSize}")
        };
    }

    internal static void GenerateLengthWrite(CodeWriter w, string countVar, int lengthSize, bool littleEndian = false)
    {
        switch (lengthSize)
        {
            case 1:
                w.AppendLine($"if ({countVar} > byte.MaxValue)");
                w.Indent();
                w.AppendLine($"throw new System.InvalidOperationException($\"Collection length {{{countVar}}} exceeds maximum for 1-byte length ({{byte.MaxValue}})\");");
                w.Outdent();
                w.AppendLine($"writer.WriteByte((byte){countVar});");
                break;
            case 2:
                w.AppendLine($"if ({countVar} > ushort.MaxValue)");
                w.Indent();
                w.AppendLine($"throw new System.InvalidOperationException($\"Collection length {{{countVar}}} exceeds maximum for 2-byte length ({{ushort.MaxValue}})\");");
                w.Outdent();
                w.AppendLine(littleEndian
                    ? $"writer.WriteUInt16LE((ushort){countVar});"
                    : $"writer.WriteUInt16((ushort){countVar});");
                break;
            case 4:
                w.AppendLine(littleEndian
                    ? $"writer.WriteUInt32LE((uint){countVar});"
                    : $"writer.WriteUInt32((uint){countVar});");
                break;
            default:
                throw new InvalidOperationException($"Invalid length size: {lengthSize}");
        }
    }

    internal static string GetEmptyCollectionExpression(ITypeSymbol collectionType, ITypeSymbol elementType)
    {
        var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (collectionType is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            var genericDef = namedType.ConstructedFrom.ToDisplayString();
            if (genericDef == "System.Collections.Generic.List<T>")
            {
                return $"new System.Collections.Generic.List<{elementTypeName}>()";
            }
        }

        return $"System.Array.Empty<{elementTypeName}>()";
    }

    // ------------------------------------------------------------------
    //  Naming
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a safe C# identifier from a type symbol (replacing generic brackets, dots, etc.).
    /// Used for generated method names like <c>DeserializeFoo</c> / <c>SerializeFoo</c>.
    /// </summary>
    internal static string GetSafeTypeName(ITypeSymbol type)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        name = name.Replace(".", "_").Replace("<", "_").Replace(">", "_").Replace("[", "_").Replace("]", "_").Replace(",", "_").Replace(" ", "").Replace("?", "");
        return name;
    }
}
