using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static RpcSourceGenerator.SerializerCodeGenUtilities;

namespace RpcSourceGenerator
{
    [Generator]
    public class PacketSerializerGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all classes marked with [PacketSerializerContext]
            var contextClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cls && cls.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetContextClassOrNull(ctx))
                .Where(static m => m is not null);

            // Combine with compilation
            var compilationAndContexts = context.CompilationProvider.Combine(contextClasses.Collect());

            // Generate source for each context
            context.RegisterSourceOutput(compilationAndContexts, static (spc, source) => Execute(source.Left, source.Right, spc));
        }

        private static ClassDeclarationSyntax? GetContextClassOrNull(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;

            // Must be partial
            if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return null;

            // Check for [PacketSerializerContext] attribute
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
            if (symbol == null)
                return null;

            var hasContextAttribute = symbol.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "PacketSerializerContextAttribute" &&
                         (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

            return hasContextAttribute ? classDeclaration : null;
        }

        private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax?> contexts, SourceProductionContext context)
        {
            if (contexts.IsDefaultOrEmpty)
                return;

            foreach (var contextDeclaration in contexts)
            {
                if (contextDeclaration == null)
                    continue;

                var semanticModel = compilation.GetSemanticModel(contextDeclaration.SyntaxTree);
                var contextSymbol = semanticModel.GetDeclaredSymbol(contextDeclaration);
                if (contextSymbol == null)
                    continue;

                // Get types from the [PacketSerializerContext(typeof(Type1), typeof(Type2), ...)] attribute
                var contextAttribute = contextSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "PacketSerializerContextAttribute");

                if (contextAttribute == null || contextAttribute.ConstructorArguments.IsEmpty)
                    continue;

                var rootTypes = new List<INamedTypeSymbol>();

                // The attribute constructor takes params Type[] types
                var typesArg = contextAttribute.ConstructorArguments[0];
                if (typesArg.Kind == TypedConstantKind.Array)
                {
                    foreach (var typeConstant in typesArg.Values)
                    {
                        if (typeConstant.Value is INamedTypeSymbol typeSymbol)
                            rootTypes.Add(typeSymbol);
                    }
                }

                if (rootTypes.Count > 0)
                {
                    // Discover all types needed (root types + their reference type properties recursively)
                    var allTypes = DiscoverAllTypes(rootTypes);
                    var source = GenerateSource(contextSymbol, rootTypes, allTypes);
                    context.AddSource($"{contextSymbol.Name}_Generated.g.cs", source);
                }
            }
        }

        private static List<INamedTypeSymbol> DiscoverAllTypes(List<INamedTypeSymbol> rootTypes)
        {
            var allTypes = new List<INamedTypeSymbol>();
            var typeSet = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var toProcess = new Queue<INamedTypeSymbol>(rootTypes);

            while (toProcess.Count > 0)
            {
                var currentType = toProcess.Dequeue();
                
                // Skip if already processed
                if (!typeSet.Add(currentType))
                    continue;

                allTypes.Add(currentType);

                // Examine properties for reference types that need serialization
                var properties = currentType.GetMembers().OfType<IPropertySymbol>()
                    .Where(p => p.DeclaredAccessibility == Accessibility.Public && 
                                (p.GetMethod != null || p.SetMethod != null))
                    .ToList();

                foreach (var prop in properties)
                {
                    var propType = prop.Type;
                    
                    // Get underlying type for nullables
                    if (propType is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullableType)
                    {
                        propType = nullableType.TypeArguments[0];
                    }

                    // Check if this property type needs its own serializer
                    if (ShouldGenerateSerializerFor(propType, out var typeToAdd))
                    {
                        toProcess.Enqueue(typeToAdd);
                    }
                    
                    // Check collection element types
                    if (IsCollectionType(propType, out var elementType) && elementType != null)
                    {
                        if (ShouldGenerateSerializerFor(elementType, out var elementTypeToAdd))
                        {
                            toProcess.Enqueue(elementTypeToAdd);
                        }
                    }
                }
            }

            return allTypes;
        }

        private static string GenerateSource(INamedTypeSymbol contextSymbol, List<INamedTypeSymbol> rootTypes, List<INamedTypeSymbol> allTypes)
        {
            var namespaceName = contextSymbol.ContainingNamespace?.ToDisplayString();
            var className = contextSymbol.Name;
            Rules.SerializerCodeGenRuleRegistry.BeginSession();

            var w = new CodeWriter();
            w.AppendLine("// <auto-generated/>");
            w.AppendLine("#nullable enable");
            w.AppendLine();
            w.AppendLine("using System;");
            w.AppendLine("using System.Buffers;");
            w.AppendLine("using Core.Infrastructure.Network;");
            w.AppendLine("using Core.Infrastructure.Network.Serialization;");
            w.AppendLine();

            if (!string.IsNullOrEmpty(namespaceName))
                w.OpenBlock($"namespace {namespaceName}");

            w.OpenBlock($"public partial class {className} : IPacketSerializerContext");

            // Generate TryDeserialize method - only for root types
            w.OpenBlock("public bool TryDeserialize(Type type, ReadOnlySpan<byte> buffer, out object? result)");
            w.AppendLine("result = null;");
            w.AppendLine("var reader = new BinaryPacketSerializer.SpanReader(buffer);");
            w.AppendLine();

            foreach (var type in rootTypes)
            {
                var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                w.OpenBlock($"if (type == typeof({fullTypeName}))");
                w.AppendLine($"result = Deserialize{GetSafeTypeName(type)}(ref reader);");
                w.AppendLine("return true;");
                w.CloseBlock();
                w.AppendLine();
            }

            w.AppendLine("return false;");
            w.CloseBlock(); // TryDeserialize
            w.AppendLine();

            // Generate TrySerialize method - only for root types
            w.OpenBlock("public bool TrySerialize(object value, IBufferWriter<byte> buffer)");
            w.AppendLine("var writer = new BinaryPacketSerializer.SpanWriter(buffer);");
            w.AppendLine();

            foreach (var type in rootTypes)
            {
                var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var safeName = GetSafeTypeName(type);
                w.OpenBlock($"if (value is {fullTypeName} val{safeName})");
                w.AppendLine($"Serialize{safeName}(val{safeName}, ref writer);");
                w.AppendLine("return true;");
                w.CloseBlock();
                w.AppendLine();
            }

            w.AppendLine("return false;");
            w.CloseBlock(); // TrySerialize
            w.AppendLine();

            // Generate type-specific Deserialize methods for ALL types
            foreach (var type in allTypes)
            {
                GenerateDeserializeMethod(w, type);
                w.AppendLine();
            }

            // Generate type-specific Serialize methods for ALL types
            foreach (var type in allTypes)
            {
                GenerateSerializeMethod(w, type);
                w.AppendLine();
            }

            // Generate collection helper methods (accumulated by CollectionCodeGen during the session)
            Rules.SerializerCodeGenRuleRegistry.EmitHelperMethods(w);

            w.CloseBlock(); // class

            if (!string.IsNullOrEmpty(namespaceName))
                w.CloseBlock(); // namespace

            return w.ToString();
        }

        private static void GenerateDeserializeMethod(CodeWriter w, INamedTypeSymbol type)
        {
            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var safeName = GetSafeTypeName(type);

            w.OpenBlock($"private {fullTypeName} Deserialize{safeName}(ref BinaryPacketSerializer.SpanReader reader)");

            // ALL public readable properties, in declaration order — drives wire read sequence.
            var allProps = type.GetMembers().OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                .ToList();

            // Phase 1 — read every field in wire order.
            // Getter-only properties are consumed and discarded; settable ones go into locals.
            foreach (var prop in allProps)
            {
                var propType = prop.Type;
                var isNullable = propType.NullableAnnotation == NullableAnnotation.Annotated;
                var underlyingType = propType;
                if (isNullable && propType is INamedTypeSymbol namedNullable && namedNullable.IsGenericType)
                    underlyingType = namedNullable.TypeArguments[0];

                var readExpr = BuildReadExpression(prop, propType, underlyingType, isNullable);

                // [ConditionalOn] — wrap in if-guard referencing the already-read local
                var conditionalInfo = GetConditionalOnInfo(prop);
                if (conditionalInfo != null)
                {
                    var (siblingName, values) = conditionalInfo.Value;
                    var conditions = string.Join(" || ", values.Select(v => $"local_{siblingName} == {v}"));

                    if (prop.SetMethod != null)
                    {
                        // Declare with default before the guard so the local is in scope for the object initializer
                        var conditionalTypeName = propType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        w.AppendLine($"{conditionalTypeName} local_{prop.Name} = default;");
                        w.AppendLine($"if ({conditions})");
                        w.Indent();
                        w.AppendLine($"local_{prop.Name} = {readExpr};");
                        w.Outdent();
                    }
                    else
                    {
                        w.AppendLine($"if ({conditions})");
                        w.Indent();
                        w.AppendLine($"_ = {readExpr}; // getter-only — consumed from stream");
                        w.Outdent();
                    }
                }
                else if (prop.SetMethod != null)
                    w.AppendLine($"var local_{prop.Name} = {readExpr};");
                else
                    w.AppendLine($"_ = {readExpr}; // getter-only — consumed from stream");
            }

            w.AppendLine();

            // Phase 2 — construct object from locals (object initializer supports required properties).
            var settableProps = allProps.Where(p => p.SetMethod != null).ToList();
            w.AppendLine($"return new {fullTypeName}");
            w.OpenBlock();
            for (var i = 0; i < settableProps.Count; i++)
            {
                var prop = settableProps[i];
                var isLast = i == settableProps.Count - 1;
                w.AppendLine($"{prop.Name} = local_{prop.Name}{(isLast ? "" : ",")}");
            }
            w.Outdent();
            w.AppendLine("};");

            w.CloseBlock(); // method
        }

        private static string BuildReadExpression(IPropertySymbol prop, ITypeSymbol propType, ITypeSymbol underlyingType, bool isNullable)
        {
            // Dispatch to ISerializerRuleCodeGen if a registered rule matches
            // (handles PascalString, CString, LittleEndian, enums, collections, byte[])
            var ruleCtx = new Rules.SourceGenPropertyContext(prop);
            var rule = Rules.SerializerCodeGenRuleRegistry.Resolve(ruleCtx);
            if (rule != null)
                return rule.EmitReadExpression(ruleCtx);

            // Custom reference type
            if (ShouldGenerateSerializerFor(underlyingType, out var customType))
            {
                var customSafeName = GetSafeTypeName(customType);
                if (HasNullPrefixed(prop))
                    return $"(reader.ReadByte() != 0 ? Deserialize{customSafeName}(ref reader) : null)";
                return isNullable
                    ? $"reader.IsAtEnd() ? null : Deserialize{customSafeName}(ref reader)"
                    : $"Deserialize{customSafeName}(ref reader)";
            }

            // Boolean, primitive (with nullable wrapping)
            var readExpr = EmitReadForType(underlyingType, HasLittleEndian(prop));
            if (isNullable)
            {
                var underlyingTypeName = underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return $"reader.IsAtEnd() ? ({underlyingTypeName}?)null : {readExpr}";
            }
            return readExpr;
        }

        private static void GenerateSerializeMethod(CodeWriter w, INamedTypeSymbol type)
        {
            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var safeName = GetSafeTypeName(type);

            w.OpenBlock($"private void Serialize{safeName}({fullTypeName} obj, ref BinaryPacketSerializer.SpanWriter writer)");

            var properties = type.GetMembers().OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                .ToList();

            foreach (var prop in properties)
            {
                // [ConditionalOn] — wrap in if-guard
                var conditionalInfo = GetConditionalOnInfo(prop);
                if (conditionalInfo != null)
                {
                    var (siblingName, values) = conditionalInfo.Value;
                    var conditions = string.Join(" || ", values.Select(v => $"obj.{siblingName} == {v}"));
                    w.OpenBlock($"if ({conditions})");
                    GenerateSerializeProperty(w, prop, $"obj.{prop.Name}");
                    w.CloseBlock();
                }
                else
                {
                    GenerateSerializeProperty(w, prop, $"obj.{prop.Name}");
                }
            }

            w.CloseBlock(); // method
        }

        private static void GenerateSerializeProperty(CodeWriter w, IPropertySymbol property, string valueExpression)
        {
            // Dispatch to ISerializerRuleCodeGen if a registered rule matches
            // (handles PascalString, CString, LittleEndian, enums, collections, byte[])
            var ruleCtx = new Rules.SourceGenPropertyContext(property);
            var rule = Rules.SerializerCodeGenRuleRegistry.Resolve(ruleCtx);
            if (rule != null)
            {
                w.AppendMultiLine(rule.EmitWriteStatement(ruleCtx, valueExpression));
                return;
            }

            var propType = property.Type;

            var underlyingType = propType;
            var needsCast = false;

            if (propType.NullableAnnotation == NullableAnnotation.Annotated && propType is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                underlyingType = namedType.TypeArguments[0];
                needsCast = true;
            }

            var value = needsCast ? $"{valueExpression}.Value" : valueExpression;

            // Handle custom reference types
            if (ShouldGenerateSerializerFor(underlyingType, out var customType))
            {
                var customTypeSafeName = GetSafeTypeName(customType);
                if (HasNullPrefixed(property))
                {
                    w.AppendLine($"if ({valueExpression} == null)");
                    w.Indent();
                    w.AppendLine("writer.WriteByte(0);");
                    w.Outdent();
                    w.AppendLine("else");
                    w.OpenBlock();
                    w.AppendLine("writer.WriteByte(1);");
                    w.AppendLine($"Serialize{customTypeSafeName}({valueExpression}, ref writer);");
                    w.CloseBlock();
                }
                else if (needsCast)
                {
                    w.OpenBlock($"if ({valueExpression} != null)");
                    w.AppendLine($"Serialize{customTypeSafeName}({value}, ref writer);");
                    w.CloseBlock();
                }
                else
                {
                    w.AppendLine($"Serialize{customTypeSafeName}({value}, ref writer);");
                }
                return;
            }

            // Primitives (with nullable wrapping)
            var writeExpr = EmitWriteForType(underlyingType, value, HasLittleEndian(property));
            if (needsCast)
            {
                w.OpenBlock($"if ({valueExpression} != null)");
                w.AppendLine($"{writeExpr};");
                w.CloseBlock();
            }
            else
            {
                w.AppendLine($"{writeExpr};");
            }
        }

        private static bool HasLittleEndian(IPropertySymbol property) =>
            property.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "LittleEndianAttribute" &&
                          (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

        private static bool HasNullPrefixed(IPropertySymbol property) =>
            property.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "NullPrefixedAttribute" &&
                          (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

        /// <summary>
        /// Returns the (PropertyName, Values[]) from a [ConditionalOn] attribute, or null if absent.
        /// </summary>
        private static (string PropertyName, long[] Values)? GetConditionalOnInfo(IPropertySymbol property)
        {
            var attr = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "ConditionalOnAttribute" &&
                                     (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));
            if (attr == null) return null;

            // First ctor arg = property name (string)
            if (attr.ConstructorArguments.Length < 2) return null;
            var propName = attr.ConstructorArguments[0].Value as string;
            if (propName == null) return null;

            // Second ctor arg = params object[] values
            var valuesArg = attr.ConstructorArguments[1];
            var values = valuesArg.Values
                .Select(v => Convert.ToInt64(v.Value))
                .ToArray();

            return (propName, values);
        }

    }
}
