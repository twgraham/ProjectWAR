using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Defines how a serialization attribute emits read/write code at compile time.
/// Each <see cref="ISerializerRuleCodeGen"/> corresponds to a runtime
/// <c>ISerializerRule</c> in the main library, ensuring both paths stay in sync.
/// </summary>
/// <remarks>
/// Implementations must NOT use reflection — they produce C# source text
/// that the source generator emits into the partial serializer context class.
/// </remarks>
public interface ISerializerRuleCodeGen
{
    /// <summary>
    /// Returns <c>true</c> if this rule applies to the given property
    /// (typically by checking for a specific attribute on the property symbol).
    /// </summary>
    bool CanHandle(SourceGenPropertyContext ctx);

    /// <summary>
    /// Returns a C# expression that reads the property value from a
    /// <c>BinaryPacketSerializer.SpanReader</c> named <c>reader</c>.
    /// </summary>
    string EmitReadExpression(SourceGenPropertyContext ctx);

    /// <summary>
    /// Returns one or more C# statements that write the property value
    /// to a <c>BinaryPacketSerializer.SpanWriter</c> named <c>writer</c>.
    /// The <paramref name="valueExpression"/> contains the C# expression that evaluates to the value.
    /// </summary>
    string EmitWriteStatement(SourceGenPropertyContext ctx, string valueExpression);

    /// <summary>
    /// Returns the fixed serialized size in bytes, or <c>null</c> if variable-length.
    /// </summary>
    int? SerializedSize(SourceGenPropertyContext ctx);
}
