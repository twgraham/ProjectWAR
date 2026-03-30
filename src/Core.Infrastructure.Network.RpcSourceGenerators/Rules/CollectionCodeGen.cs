using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Source-gen companion for collection (arrays, <c>List&lt;T&gt;</c>, etc.) and <c>byte[]</c> properties.
/// Reads <c>[PacketLength]</c>, <c>[FixedLength]</c>, and <c>[SizedEntry]</c> modifier attributes
/// to configure the wire format.
///
/// <para><b>Stateful</b>: accumulates helper-method registrations during a generation session.
/// Call <see cref="Reset"/> before each session and <see cref="EmitHelperMethods"/> after all
/// properties have been processed.</para>
/// </summary>
public sealed class CollectionCodeGen : ISerializerRuleCodeGen
{
    // --- Accumulated state (per generation session) ---

    private readonly HashSet<string> _deserializeMethods = new();
    private readonly HashSet<string> _serializeMethods = new();

    private readonly Dictionary<string, (ITypeSymbol CollectionType, ITypeSymbol ElementType,
        int LengthSize, int? FixedCount, int? SizedEntryWidth, bool SizedEntryLE, bool LengthLE)> _collectionInfo = new();

    /// <summary>
    /// Clears all accumulated helper-method registrations.
    /// Call at the start of each code-generation session.
    /// </summary>
    public void Reset()
    {
        _deserializeMethods.Clear();
        _serializeMethods.Clear();
        _collectionInfo.Clear();
    }

    // ------------------------------------------------------------------
    //  ISerializerRuleCodeGen
    // ------------------------------------------------------------------

    public bool CanHandle(SourceGenPropertyContext ctx)
    {
        var type = ctx.UnderlyingType;

        // byte[]
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            return true;

        // Other arrays and generic collections (List<T>, IList<T>, ICollection<T>, IEnumerable<T>)
        return PacketSerializerGenerator.IsCollectionType(type, out _);
    }

    public string EmitReadExpression(SourceGenPropertyContext ctx)
    {
        var type = ctx.UnderlyingType;

        // byte[] — inline expression (no helper method needed)
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            var fixedLen = GetFixedLength(ctx);
            if (fixedLen.HasValue)
                return $"reader.ReadFixedByteArray({fixedLen.Value})";
            return $"reader.ReadByteArray({GetPacketLengthSize(ctx)})";
        }

        // Collection — register helper method and return call expression
        PacketSerializerGenerator.IsCollectionType(type, out var elementType);
        var lengthSize = GetPacketLengthSize(ctx);
        var lengthLE = GetPacketLengthLE(ctx);
        var fixedCount = GetFixedLength(ctx);
        var (sizedEntryWidth, sizedEntryLE) = GetSizedEntryInfo(ctx);

        var methodName = RegisterDeserializeMethod(
            type, elementType!, lengthSize, fixedCount, sizedEntryWidth, sizedEntryLE, lengthLE);
        return $"{methodName}(ref reader)";
    }

    public string EmitWriteStatement(SourceGenPropertyContext ctx, string valueExpression)
    {
        var type = ctx.UnderlyingType;

        // byte[] — inline statement
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            var fixedLen = GetFixedLength(ctx);
            if (fixedLen.HasValue)
                return $"writer.WriteFixedByteArray({valueExpression}, {fixedLen.Value});";
            return $"writer.WriteByteArray({valueExpression}, {GetPacketLengthSize(ctx)});";
        }

        // Collection — register helper method and return call statement
        PacketSerializerGenerator.IsCollectionType(type, out var elementType);
        var lengthSize = GetPacketLengthSize(ctx);
        var lengthLE = GetPacketLengthLE(ctx);
        var fixedCount = GetFixedLength(ctx);
        var (sizedEntryWidth, sizedEntryLE) = GetSizedEntryInfo(ctx);

        var methodName = RegisterSerializeMethod(
            type, elementType!, lengthSize, fixedCount, sizedEntryWidth, sizedEntryLE, lengthLE);
        return $"{methodName}(ref writer, {valueExpression});";
    }

    public int? SerializedSize(SourceGenPropertyContext ctx)
    {
        var type = ctx.UnderlyingType;

        // byte[] with [FixedLength] has a known size
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            return GetFixedLength(ctx);

        // Collections are always variable-length
        return null;
    }

    // ------------------------------------------------------------------
    //  Helper-method emission (called once after all properties processed)
    // ------------------------------------------------------------------

    /// <summary>
    /// Emits all accumulated collection helper methods into the generated source.
    /// </summary>
    public void EmitHelperMethods(StringBuilder sb)
    {
        foreach (var methodName in _deserializeMethods)
        {
            var info = _collectionInfo[methodName];
            EmitDeserializeMethod(sb, methodName,
                info.CollectionType, info.ElementType, info.LengthSize,
                info.FixedCount, info.SizedEntryWidth, info.SizedEntryLE, info.LengthLE);
            sb.AppendLine();
        }

        foreach (var methodName in _serializeMethods)
        {
            var info = _collectionInfo[methodName];
            EmitSerializeMethod(sb, methodName,
                info.CollectionType, info.ElementType, info.LengthSize,
                info.FixedCount, info.SizedEntryWidth, info.SizedEntryLE, info.LengthLE);
            sb.AppendLine();
        }
    }

    // ------------------------------------------------------------------
    //  Registration (deduplicates methods by name)
    // ------------------------------------------------------------------

    private string RegisterDeserializeMethod(
        ITypeSymbol collectionType, ITypeSymbol elementType,
        int lengthSize, int? fixedCount, int? sizedEntryWidth, bool sizedEntryLE, bool lengthLE)
    {
        var safeName = PacketSerializerGenerator.GetSafeTypeName(collectionType);
        var leSuffix = lengthLE ? "_lle" : "";
        var methodName = fixedCount.HasValue
            ? $"DeserializeCollection_{safeName}_fixed_{fixedCount.Value}"
            : sizedEntryWidth.HasValue
                ? $"DeserializeCollection_{safeName}_{lengthSize}_sized_{sizedEntryWidth.Value}{(sizedEntryLE ? "_le" : "")}{leSuffix}"
                : $"DeserializeCollection_{safeName}_{lengthSize}{leSuffix}";

        if (_deserializeMethods.Add(methodName))
            _collectionInfo[methodName] = (collectionType, elementType, lengthSize, fixedCount, sizedEntryWidth, sizedEntryLE, lengthLE);

        return methodName;
    }

    private string RegisterSerializeMethod(
        ITypeSymbol collectionType, ITypeSymbol elementType,
        int lengthSize, int? fixedCount, int? sizedEntryWidth, bool sizedEntryLE, bool lengthLE)
    {
        var safeName = PacketSerializerGenerator.GetSafeTypeName(collectionType);
        var leSuffix = lengthLE ? "_lle" : "";
        var methodName = fixedCount.HasValue
            ? $"SerializeCollection_{safeName}_fixed_{fixedCount.Value}"
            : sizedEntryWidth.HasValue
                ? $"SerializeCollection_{safeName}_{lengthSize}_sized_{sizedEntryWidth.Value}{(sizedEntryLE ? "_le" : "")}{leSuffix}"
                : $"SerializeCollection_{safeName}_{lengthSize}{leSuffix}";

        if (_serializeMethods.Add(methodName))
            _collectionInfo[methodName] = (collectionType, elementType, lengthSize, fixedCount, sizedEntryWidth, sizedEntryLE, lengthLE);

        return methodName;
    }

    // ------------------------------------------------------------------
    //  Code emission for individual collection helper methods
    // ------------------------------------------------------------------

    private static void EmitDeserializeMethod(
        StringBuilder sb, string methodName,
        ITypeSymbol collectionType, ITypeSymbol elementType,
        int lengthSize, int? fixedCount, int? sizedEntryWidth, bool sizedEntryLE, bool lengthLE)
    {
        var collectionTypeName = collectionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        sb.AppendLine($"        private {collectionTypeName} {methodName}(ref BinaryPacketSerializer.SpanReader reader)");
        sb.AppendLine("        {");

        if (fixedCount.HasValue)
        {
            sb.AppendLine($"            const int length = {fixedCount.Value};");
            sb.AppendLine($"            if (length == 0) return {PacketSerializerGenerator.GetEmptyCollectionExpression(collectionType, elementType)};");
        }
        else
        {
            sb.AppendLine($"            var length = {PacketSerializerGenerator.GenerateLengthRead(lengthSize, lengthLE)};");
            sb.AppendLine($"            if (length == 0) return {PacketSerializerGenerator.GetEmptyCollectionExpression(collectionType, elementType)};");
        }

        if (sizedEntryWidth.HasValue)
            sb.AppendLine($"            _ = {PacketSerializerGenerator.GenerateEntrySizeRead(sizedEntryWidth.Value, sizedEntryLE)}; // entry size");

        sb.AppendLine();
        sb.AppendLine($"            var array = new {elementTypeName}[length];");
        sb.AppendLine("            for (int i = 0; i < length; i++)");
        sb.AppendLine("            {");

        sb.Append("                array[i] = ");
        PacketSerializerGenerator.GenerateElementRead(sb, elementType);
        sb.AppendLine(";");

        sb.AppendLine("            }");
        sb.AppendLine();

        if (collectionType is IArrayTypeSymbol)
        {
            sb.AppendLine("            return array;");
        }
        else if (collectionType is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            var genericDef = namedType.ConstructedFrom.ToDisplayString();
            sb.AppendLine(genericDef == "System.Collections.Generic.List<T>"
                ? $"            return new System.Collections.Generic.List<{elementTypeName}>(array);"
                : "            return array;");
        }

        sb.AppendLine("        }");
    }

    private static void EmitSerializeMethod(
        StringBuilder sb, string methodName,
        ITypeSymbol collectionType, ITypeSymbol elementType,
        int lengthSize, int? fixedCount, int? sizedEntryWidth, bool sizedEntryLE, bool lengthLE)
    {
        var collectionTypeName = collectionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        sb.AppendLine($"        private void {methodName}(ref BinaryPacketSerializer.SpanWriter writer, {collectionTypeName} collection)");
        sb.AppendLine("        {");

        string countExpression = collectionType is IArrayTypeSymbol ? "collection.Length" : "collection.Count";

        if (fixedCount.HasValue)
        {
            sb.AppendLine($"            var count = {countExpression};");
            sb.AppendLine($"            if (count != {fixedCount.Value})");
            sb.AppendLine($"                throw new System.InvalidOperationException($\"Collection length {{count}} does not match [FixedLength({fixedCount.Value})]\");");
        }
        else
        {
            sb.AppendLine($"            var count = {countExpression};");
            PacketSerializerGenerator.GenerateLengthWrite(sb, "count", lengthSize, lengthLE);
        }

        if (sizedEntryWidth.HasValue)
        {
            var wireSize = PacketSerializerGenerator.TryComputeWireSize(elementType);
            if (wireSize.HasValue)
            {
                PacketSerializerGenerator.GenerateEntrySizeWrite(sb, wireSize.Value, sizedEntryWidth.Value, sizedEntryLE);
            }
            else
            {
                sb.AppendLine($"#warning Cannot compute fixed wire size for element type '{elementType.Name}'. [SizedEntry] entry size will be written as 0.");
                PacketSerializerGenerator.GenerateEntrySizeWrite(sb, 0, sizedEntryWidth.Value, sizedEntryLE);
            }
        }

        sb.AppendLine();
        sb.AppendLine("            foreach (var item in collection)");
        sb.AppendLine("            {");

        sb.Append("                ");
        PacketSerializerGenerator.GenerateElementWrite(sb, elementType, "item");
        sb.AppendLine(";");

        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }

    // ------------------------------------------------------------------
    //  Attribute reading helpers
    // ------------------------------------------------------------------

    private static int GetPacketLengthSize(SourceGenPropertyContext ctx)
    {
        var attr = ctx.GetAttribute("PacketLengthAttribute");
        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is int byteCount)
            return byteCount;
        return 1; // default 1-byte length prefix
    }

    private static bool GetPacketLengthLE(SourceGenPropertyContext ctx)
    {
        var attr = ctx.GetAttribute("PacketLengthAttribute");
        if (attr == null) return false;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "LittleEndian" && named.Value.Value is bool le)
                return le;
        }
        return false;
    }

    private static int? GetFixedLength(SourceGenPropertyContext ctx)
    {
        var attr = ctx.GetAttribute("FixedLengthAttribute");
        if (attr is { ConstructorArguments.Length: > 0 } && attr.ConstructorArguments[0].Value is int len)
            return len;
        return null;
    }

    private static (int? Width, bool LittleEndian) GetSizedEntryInfo(SourceGenPropertyContext ctx)
    {
        var attr = ctx.GetAttribute("SizedEntryAttribute");
        if (attr == null) return (null, false);

        int width = 2; // attribute default
        bool littleEndian = false;

        if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int byteCount)
            width = byteCount;
        if (attr.ConstructorArguments.Length > 1 && attr.ConstructorArguments[1].Value is bool le)
            littleEndian = le;

        return (width, littleEndian);
    }
}
