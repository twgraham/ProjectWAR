using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace RpcSourceGenerator.Rules;

/// <summary>
/// Provides metadata about a property being code-generated for serialization.
/// Mirrors <c>SerializerPropertyContext</c> from the runtime library but
/// operates on Roslyn <see cref="IPropertySymbol"/> / <see cref="ITypeSymbol"/>.
/// </summary>
public sealed class SourceGenPropertyContext
{
    public IPropertySymbol Property { get; }

    /// <summary>The declared type (may be <c>Nullable&lt;T&gt;</c>).</summary>
    public ITypeSymbol PropertyType { get; }

    /// <summary>The underlying type after unwrapping <c>Nullable&lt;T&gt;</c>.</summary>
    public ITypeSymbol UnderlyingType { get; }

    /// <summary>Whether the property is annotated as nullable.</summary>
    public bool IsNullable { get; }

    /// <summary>Whether the property is a <c>Nullable&lt;T&gt;</c> value type (e.g. <c>int?</c>).</summary>
    public bool IsNullableValueType { get; }

    /// <summary>All attributes applied to the property.</summary>
    public ImmutableArray<AttributeData> Attributes { get; }

    public SourceGenPropertyContext(IPropertySymbol property)
    {
        Property = property;
        PropertyType = property.Type;
        Attributes = property.GetAttributes();

        // Unwrap Nullable<T>
        if (property.Type is INamedTypeSymbol { IsGenericType: true } namedType &&
            namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            UnderlyingType = namedType.TypeArguments[0];
            IsNullable = true;
            IsNullableValueType = true;
        }
        else
        {
            UnderlyingType = property.Type;
            IsNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated;
            IsNullableValueType = false;
        }
    }

    /// <summary>
    /// Returns the <see cref="AttributeData"/> for an attribute whose class name matches
    /// <paramref name="attributeName"/> and belongs to the <c>Core.Infrastructure.Network</c> namespace.
    /// </summary>
    public AttributeData? GetAttribute(string attributeName)
        => Attributes.FirstOrDefault(a =>
            a.AttributeClass?.Name == attributeName &&
            (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

    /// <summary>
    /// Returns <c>true</c> if the property has an attribute whose class name matches <paramref name="attributeName"/>.
    /// </summary>
    public bool HasAttribute(string attributeName)
        => GetAttribute(attributeName) != null;
}
