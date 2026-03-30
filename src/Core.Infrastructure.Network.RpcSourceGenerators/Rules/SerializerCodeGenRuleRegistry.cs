using System.Collections.Generic;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Central registry of all <see cref="ISerializerRuleCodeGen"/> implementations.
/// The source generator queries this registry for each property — the first rule
/// whose <see cref="ISerializerRuleCodeGen.CanHandle"/> returns <c>true</c> wins.
/// </summary>
/// <remarks>
/// To add a new serialization rule:
/// 1. Create the attribute + <see cref="Serialization.ISerializerRule"/> implementation in the runtime library.
/// 2. Create an <see cref="ISerializerRuleCodeGen"/> implementation in this project.
/// 3. Register it here.
///
/// Stateful rules (e.g. <see cref="CollectionCodeGen"/>) must be reset between generation
/// sessions via <see cref="BeginSession"/> and flushed via <see cref="EmitHelperMethods"/>.
/// </remarks>
public static class SerializerCodeGenRuleRegistry
{
    private static readonly CollectionCodeGen CollectionRule = new();

    private static readonly List<ISerializerRuleCodeGen> Rules =
    [
        new PascalStringCodeGen(),
        new CStringCodeGen(),
        new LittleEndianCodeGen(),
        new EnumCodeGen(),
        CollectionRule,
    ];

    /// <summary>
    /// Resets all stateful rules. Call once at the start of each code-generation session
    /// (i.e. per <c>[PacketSerializerContext]</c> class).
    /// </summary>
    public static void BeginSession() => CollectionRule.Reset();

    /// <summary>
    /// Emits any deferred helper methods accumulated by stateful rules during the session.
    /// Call once after all type serializers have been generated.
    /// </summary>
    internal static void EmitHelperMethods(CodeWriter w) => CollectionRule.EmitHelperMethods(w);

    /// <summary>
    /// Returns the first <see cref="ISerializerRuleCodeGen"/> that can handle the given property,
    /// or <c>null</c> if no rule matches (caller falls back to built-in logic).
    /// </summary>
    public static ISerializerRuleCodeGen? Resolve(SourceGenPropertyContext ctx)
    {
        foreach (var rule in Rules)
        {
            if (rule.CanHandle(ctx))
                return rule;
        }
        return null;
    }
}
