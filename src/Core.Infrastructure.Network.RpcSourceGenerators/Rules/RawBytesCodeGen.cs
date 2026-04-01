using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Source-gen companion for <c>[RawBytes]</c>.
/// Emits direct calls to <c>reader.ReadRawBytes()</c>/<c>writer.WriteRawBytes()</c>.
/// Matches only <c>byte[]</c> properties annotated with <c>[RawBytes]</c>.
/// </summary>
/// <remarks>
/// Must be registered <b>before</b> <see cref="CollectionCodeGen"/> so that
/// <c>byte[]</c> + <c>[RawBytes]</c> is intercepted before the generic collection path.
/// </remarks>
public sealed class RawBytesCodeGen : ISerializerRuleCodeGen
{
    public bool CanHandle(SourceGenPropertyContext ctx)
        => ctx.UnderlyingType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte }
           && ctx.HasAttribute("RawBytesAttribute");

    public string EmitReadExpression(SourceGenPropertyContext ctx)
        => "reader.ReadRawBytes()";

    public string EmitWriteStatement(SourceGenPropertyContext ctx, string valueExpression)
        => $"writer.WriteRawBytes({valueExpression});";

    /// <summary>Always variable-length (reads remainder of buffer).</summary>
    public int? SerializedSize(SourceGenPropertyContext ctx) => null;
}
