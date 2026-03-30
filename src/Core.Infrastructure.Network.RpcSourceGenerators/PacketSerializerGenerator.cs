using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

        private static string GenerateSource(INamedTypeSymbol contextSymbol, List<INamedTypeSymbol> rootTypes, List<INamedTypeSymbol> allTypes)
        {
            var namespaceName = contextSymbol.ContainingNamespace?.ToDisplayString();
            var className = contextSymbol.Name;
            Rules.SerializerCodeGenRuleRegistry.BeginSession();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Buffers;");
            sb.AppendLine("using Core.Infrastructure.Network;");
            sb.AppendLine("using Core.Infrastructure.Network.Serialization;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            // Generate partial class implementing IPacketSerializerContext
            sb.AppendLine($"    public partial class {className} : IPacketSerializerContext");
            sb.AppendLine("    {");
            
            // Generate TryDeserialize method - only for root types
            sb.AppendLine("        public bool TryDeserialize(Type type, ReadOnlySpan<byte> buffer, out object? result)");
            sb.AppendLine("        {");
            sb.AppendLine("            result = null;");
            sb.AppendLine("            var reader = new BinaryPacketSerializer.SpanReader(buffer);");
            sb.AppendLine();

            foreach (var type in rootTypes)
            {
                var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                sb.AppendLine($"            if (type == typeof({fullTypeName}))");
                sb.AppendLine("            {");
                sb.AppendLine($"                result = Deserialize{GetSafeTypeName(type)}(ref reader);");
                sb.AppendLine("                return true;");
                sb.AppendLine("            }");
                sb.AppendLine();
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generate TrySerialize method - only for root types
            sb.AppendLine("        public bool TrySerialize(object value, IBufferWriter<byte> buffer)");
            sb.AppendLine("        {");
            sb.AppendLine("            var writer = new BinaryPacketSerializer.SpanWriter(buffer);");
            sb.AppendLine();

            foreach (var type in rootTypes)
            {
                var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var safeName = GetSafeTypeName(type);
                sb.AppendLine($"            if (value is {fullTypeName} val{safeName})");
                sb.AppendLine("            {");
                sb.AppendLine($"                Serialize{safeName}(val{safeName}, ref writer);");
                sb.AppendLine("                return true;");
                sb.AppendLine("            }");
                sb.AppendLine();
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generate type-specific Deserialize methods for ALL types
            foreach (var type in allTypes)
            {
                GenerateDeserializeMethod(sb, type);
                sb.AppendLine();
            }

            // Generate type-specific Serialize methods for ALL types
            foreach (var type in allTypes)
            {
                GenerateSerializeMethod(sb, type);
                sb.AppendLine();
            }

            // Generate collection helper methods (accumulated by CollectionCodeGen during the session)
            Rules.SerializerCodeGenRuleRegistry.EmitHelperMethods(sb);

            // SpanReader and SpanWriter are internal in Core.Infrastructure.Network.BinaryPacketSerializer
            // and will be used by the generated code

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static void GenerateDeserializeMethod(StringBuilder sb, INamedTypeSymbol type)
        {
            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var safeName = GetSafeTypeName(type);
            sb.AppendLine($"        private {fullTypeName} Deserialize{safeName}(ref BinaryPacketSerializer.SpanReader reader)");
            sb.AppendLine("        {");

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
                        sb.AppendLine($"            {conditionalTypeName} local_{prop.Name} = default;");
                        sb.AppendLine($"            if ({conditions})");
                        sb.AppendLine($"                local_{prop.Name} = {readExpr};");
                    }
                    else
                    {
                        sb.AppendLine($"            if ({conditions})");
                        sb.AppendLine($"                _ = {readExpr}; // getter-only — consumed from stream");
                    }
                }
                else if (prop.SetMethod != null)
                    sb.AppendLine($"            var local_{prop.Name} = {readExpr};");
                else
                    sb.AppendLine($"            _ = {readExpr}; // getter-only — consumed from stream");
            }

            sb.AppendLine();

            // Phase 2 — construct object from locals (object initializer supports required properties).
            var settableProps = allProps.Where(p => p.SetMethod != null).ToList();
            sb.AppendLine($"            return new {fullTypeName}");
            sb.AppendLine("            {");
            for (var i = 0; i < settableProps.Count; i++)
            {
                var prop = settableProps[i];
                var isLast = i == settableProps.Count - 1;
                sb.AppendLine($"                {prop.Name} = local_{prop.Name}{(isLast ? "" : ",")}");
            }
            sb.AppendLine("            };");
            sb.AppendLine("        }");
        }

        private static string BuildReadExpression(IPropertySymbol prop, ITypeSymbol propType, ITypeSymbol underlyingType, bool isNullable)
        {
            // Dispatch to ISerializerRuleCodeGen if a registered rule matches
            // (handles PascalString, CString, LittleEndian, collections, byte[])
            var ruleCtx = new Rules.SourceGenPropertyContext(prop);
            var rule = Rules.SerializerCodeGenRuleRegistry.Resolve(ruleCtx);
            if (rule != null)
                return rule.EmitReadExpression(ruleCtx);

            // Enum
            if (underlyingType.TypeKind == TypeKind.Enum)
            {
                var enumTypeName = underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return isNullable
                    ? $"reader.IsAtEnd() ? ({enumTypeName}?)null : ({enumTypeName})reader.ReadByte()"
                    : $"({enumTypeName})reader.ReadByte()";
            }

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

            // Boolean
            if (underlyingType.SpecialType == SpecialType.System_Boolean)
                return isNullable ? "reader.IsAtEnd() ? (bool?)null : (reader.ReadByte() != 0)" : "(reader.ReadByte() != 0)";

            // Nullable primitive
            if (isNullable)
            {
                var underlyingTypeName = underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var sb2 = new StringBuilder($"reader.IsAtEnd() ? ({underlyingTypeName}?)null : reader.");
                GenerateReadExpressionInline(sb2, propType, HasLittleEndian(prop));
                return sb2.ToString();
            }

            // Primitive
            var sbp = new StringBuilder("reader.");
            GenerateReadExpressionInline(sbp, propType, HasLittleEndian(prop));
            return sbp.ToString();
        }

        private static void GenerateReadExpressionInline(StringBuilder sb, ITypeSymbol type, bool littleEndian = false)
        {
            var underlyingType = type;
            if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                underlyingType = namedType.TypeArguments[0];
            }

            var typeName = underlyingType.SpecialType switch
            {
                SpecialType.System_Byte => "ReadByte()",
                SpecialType.System_SByte => "ReadSByte()",
                SpecialType.System_Int16 => littleEndian ? "ReadInt16LE()" : "ReadInt16()",
                SpecialType.System_UInt16 => littleEndian ? "ReadUInt16LE()" : "ReadUInt16()",
                SpecialType.System_Int32 => littleEndian ? "ReadInt32LE()" : "ReadInt32()",
                SpecialType.System_UInt32 => littleEndian ? "ReadUInt32LE()" : "ReadUInt32()",
                SpecialType.System_Int64 => littleEndian ? "ReadInt64LE()" : "ReadInt64()",
                SpecialType.System_UInt64 => littleEndian ? "ReadUInt64LE()" : "ReadUInt64()",
                SpecialType.System_Single => littleEndian ? "ReadFloatLE()" : "ReadFloat()",
                SpecialType.System_Double => littleEndian ? "ReadDoubleLE()" : "ReadDouble()",
                SpecialType.System_Boolean => "ReadByte() != 0",
                SpecialType.System_String => "ReadString()",
                _ => "null /* unsupported type */"
            };

            sb.Append(typeName);
        }

        private static void GenerateSerializeMethod(StringBuilder sb, INamedTypeSymbol type)
        {
            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var safeName = GetSafeTypeName(type);
            sb.AppendLine($"        private void Serialize{safeName}({fullTypeName} obj, ref BinaryPacketSerializer.SpanWriter writer)");
            sb.AppendLine("        {");

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
                    sb.AppendLine($"            if ({conditions})");
                    sb.AppendLine("            {");
                    var inner = new StringBuilder();
                    GenerateSerializeProperty(inner, prop, $"obj.{prop.Name}");
                    // Indent the inner content by 4 extra spaces
                    foreach (var line in inner.ToString().Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            sb.AppendLine($"    {line.TrimEnd()}");
                    }
                    sb.AppendLine("            }");
                }
                else
                {
                    GenerateSerializeProperty(sb, prop, $"obj.{prop.Name}");
                }
            }

            sb.AppendLine("        }");
        }

        private static void GenerateSerializeProperty(StringBuilder sb, IPropertySymbol property, string valueExpression)
        {
            // Dispatch to ISerializerRuleCodeGen if a registered rule matches
            // (handles PascalString, CString, LittleEndian, collections, byte[])
            var ruleCtx = new Rules.SourceGenPropertyContext(property);
            var rule = Rules.SerializerCodeGenRuleRegistry.Resolve(ruleCtx);
            if (rule != null)
            {
                var statements = rule.EmitWriteStatement(ruleCtx, valueExpression);
                foreach (var line in statements.Split('\n'))
                {
                    var trimmed = line.TrimEnd('\r');
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        sb.AppendLine($"            {trimmed}");
                }
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

            // Enum
            if (underlyingType.TypeKind == TypeKind.Enum)
            {
                if (needsCast)
                {
                    sb.AppendLine($"            if ({valueExpression} != null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                writer.WriteByte((byte){value});");
                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine($"            writer.WriteByte((byte){value});");
                }
                return;
            }

            // Handle custom reference types
            if (ShouldGenerateSerializerFor(underlyingType, out var customType))
            {
                var customTypeSafeName = GetSafeTypeName(customType);
                if (HasNullPrefixed(property))
                {
                    sb.AppendLine($"            if ({valueExpression} == null)");
                    sb.AppendLine($"                writer.WriteByte(0);");
                    sb.AppendLine($"            else");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                writer.WriteByte(1);");
                    sb.AppendLine($"                Serialize{customTypeSafeName}({valueExpression}, ref writer);");
                    sb.AppendLine("            }");
                }
                else if (needsCast)
                {
                    sb.AppendLine($"            if ({valueExpression} != null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                Serialize{customTypeSafeName}({value}, ref writer);");
                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine($"            Serialize{customTypeSafeName}({value}, ref writer);");
                }
                return;
            }

            // Handle nullable primitives
            if (needsCast)
            {
                sb.AppendLine($"            if ({valueExpression} != null)");
                sb.AppendLine("            {");
                sb.Append("                writer.");
                GenerateWriteExpressionInline(sb, propType, value, HasLittleEndian(property));
                sb.AppendLine(";");
                sb.AppendLine("            }");
            }
            else
            {
                sb.Append("            writer.");
                GenerateWriteExpressionInline(sb, propType, value, HasLittleEndian(property));
                sb.AppendLine(";");
            }
        }
        private static void GenerateWriteExpressionInline(StringBuilder sb, ITypeSymbol type, string valueExpression, bool littleEndian = false)
        {
            var underlyingType = type;
            if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                underlyingType = namedType.TypeArguments[0];
            }

            var methodName = underlyingType.SpecialType switch
            {
                SpecialType.System_Byte => $"WriteByte({valueExpression})",
                SpecialType.System_SByte => $"WriteSByte({valueExpression})",
                SpecialType.System_Int16 => littleEndian ? $"WriteInt16LE({valueExpression})" : $"WriteInt16({valueExpression})",
                SpecialType.System_UInt16 => littleEndian ? $"WriteUInt16LE({valueExpression})" : $"WriteUInt16({valueExpression})",
                SpecialType.System_Int32 => littleEndian ? $"WriteInt32LE({valueExpression})" : $"WriteInt32({valueExpression})",
                SpecialType.System_UInt32 => littleEndian ? $"WriteUInt32LE({valueExpression})" : $"WriteUInt32({valueExpression})",
                SpecialType.System_Int64 => littleEndian ? $"WriteInt64LE({valueExpression})" : $"WriteInt64({valueExpression})",
                SpecialType.System_UInt64 => littleEndian ? $"WriteUInt64LE({valueExpression})" : $"WriteUInt64({valueExpression})",
                SpecialType.System_Single => littleEndian ? $"WriteFloatLE({valueExpression})" : $"WriteFloat({valueExpression})",
                SpecialType.System_Double => littleEndian ? $"WriteDoubleLE({valueExpression})" : $"WriteDouble({valueExpression})",
                SpecialType.System_Boolean => $"WriteByte((byte)({valueExpression} ? 1 : 0))",
                SpecialType.System_String => $"WriteString({valueExpression})",
                _ => "/* unsupported type */"
            };

            sb.Append(methodName);
        }

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
                        or "System.Collections.Generic.ICollection<T>"
                        or "System.Collections.Generic.IEnumerable<T>")
                    {
                        elementType = namedType.TypeArguments[0];
                        return true;
                    }

                    break;
                }
            }

            return false;
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

        private static int? TryComputePropertyWireSize(IPropertySymbol prop)
        {
            // Check ISerializerRuleCodeGen registry first
            // (handles PascalString, CString, LittleEndian, collections, byte[])
            var ruleCtx = new Rules.SourceGenPropertyContext(prop);
            var rule = Rules.SerializerCodeGenRuleRegistry.Resolve(ruleCtx);
            if (rule != null)
                return rule.SerializedSize(ruleCtx);

            var propType = prop.Type;
            var underlyingType = propType;
            if (propType.NullableAnnotation == NullableAnnotation.Annotated && propType is INamedTypeSymbol namedNullable && namedNullable.IsGenericType)
                underlyingType = namedNullable.TypeArguments[0];

            // Enum
            if (underlyingType.TypeKind == TypeKind.Enum)
                return 1;

            // Custom type — recurse
            if (underlyingType is INamedTypeSymbol customType && ShouldGenerateSerializerFor(underlyingType, out _))
                return TryComputeWireSize(customType);

            // Primitive / boolean
            return TryComputeWireSize(underlyingType);
        }

        internal static void GenerateEntrySizeWrite(StringBuilder sb, int value, int byteWidth, bool littleEndian)
        {
            switch (byteWidth)
            {
                case 1:
                    sb.AppendLine($"            writer.WriteByte({value});");
                    break;
                case 2:
                    sb.AppendLine(littleEndian
                        ? $"            writer.WriteUInt16LE({value});"
                        : $"            writer.WriteUInt16({value});");
                    break;
                case 4:
                    sb.AppendLine(littleEndian
                        ? $"            writer.WriteUInt32LE({(uint)value});"
                        : $"            writer.WriteUInt32({(uint)value});");
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

        internal static void GenerateLengthWrite(StringBuilder sb, string countVar, int lengthSize, bool littleEndian = false)
        {
            switch (lengthSize)
            {
                case 1:
                    sb.AppendLine($"            if ({countVar} > byte.MaxValue)");
                    sb.AppendLine($"                throw new System.InvalidOperationException($\"Collection length {{{countVar}}} exceeds maximum for 1-byte length ({{byte.MaxValue}})\");");
                    sb.AppendLine($"            writer.WriteByte((byte){countVar});");
                    break;
                case 2:
                    sb.AppendLine($"            if ({countVar} > ushort.MaxValue)");
                    sb.AppendLine($"                throw new System.InvalidOperationException($\"Collection length {{{countVar}}} exceeds maximum for 2-byte length ({{ushort.MaxValue}})\");");
                    sb.AppendLine(littleEndian
                        ? $"            writer.WriteUInt16LE((ushort){countVar});"
                        : $"            writer.WriteUInt16((ushort){countVar});");
                    break;
                case 4:
                    sb.AppendLine(littleEndian
                        ? $"            writer.WriteUInt32LE((uint){countVar});"
                        : $"            writer.WriteUInt32((uint){countVar});");
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

        internal static void GenerateElementRead(StringBuilder sb, ITypeSymbol elementType)
        {
            // Handle custom reference types
            if (ShouldGenerateSerializerFor(elementType, out var customType))
            {
                var customTypeSafeName = GetSafeTypeName(customType);
                sb.Append($"Deserialize{customTypeSafeName}(ref reader)");
                return;
            }
            
            // Handle nested collections
            if (IsCollectionType(elementType, out _))
            {
                // For nested collections, recursively call the collection deserialize method
                // This will be handled by the tracker system
                throw new NotSupportedException("Nested collections are not yet supported in discrete methods");
            }
            
            // Handle enums
            if (elementType.TypeKind == TypeKind.Enum)
            {
                var enumTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                sb.Append($"({enumTypeName})reader.ReadByte()");
                return;
            }
            
            // Handle primitives
            var readMethod = elementType.SpecialType switch
            {
                SpecialType.System_Byte => "reader.ReadByte()",
                SpecialType.System_SByte => "reader.ReadSByte()",
                SpecialType.System_Int16 => "reader.ReadInt16()",
                SpecialType.System_UInt16 => "reader.ReadUInt16()",
                SpecialType.System_Int32 => "reader.ReadInt32()",
                SpecialType.System_UInt32 => "reader.ReadUInt32()",
                SpecialType.System_Int64 => "reader.ReadInt64()",
                SpecialType.System_UInt64 => "reader.ReadUInt64()",
                SpecialType.System_Single => "reader.ReadFloat()",
                SpecialType.System_Double => "reader.ReadDouble()",
                SpecialType.System_Boolean => "(reader.ReadByte() != 0)",
                SpecialType.System_String => "reader.ReadString()",
                _ => throw new NotSupportedException($"Element type {elementType.Name} is not supported")
            };
            
            sb.Append(readMethod);
        }

        internal static void GenerateElementWrite(StringBuilder sb, ITypeSymbol elementType, string itemVar)
        {
            // Handle custom reference types
            if (ShouldGenerateSerializerFor(elementType, out var customType))
            {
                var customTypeSafeName = GetSafeTypeName(customType);
                sb.Append($"Serialize{customTypeSafeName}({itemVar}, ref writer)");
                return;
            }
            
            // Handle nested collections
            if (IsCollectionType(elementType, out _))
            {
                // For nested collections, would need to call collection serialize method
                throw new NotSupportedException("Nested collections are not yet supported in discrete methods");
            }
            
            // Handle enums
            if (elementType.TypeKind == TypeKind.Enum)
            {
                sb.Append($"writer.WriteByte((byte){itemVar})");
                return;
            }
            
            // Handle primitives
            var writeMethod = elementType.SpecialType switch
            {
                SpecialType.System_Byte => $"writer.WriteByte({itemVar})",
                SpecialType.System_SByte => $"writer.WriteSByte({itemVar})",
                SpecialType.System_Int16 => $"writer.WriteInt16({itemVar})",
                SpecialType.System_UInt16 => $"writer.WriteUInt16({itemVar})",
                SpecialType.System_Int32 => $"writer.WriteInt32({itemVar})",
                SpecialType.System_UInt32 => $"writer.WriteUInt32({itemVar})",
                SpecialType.System_Int64 => $"writer.WriteInt64({itemVar})",
                SpecialType.System_UInt64 => $"writer.WriteUInt64({itemVar})",
                SpecialType.System_Single => $"writer.WriteFloat({itemVar})",
                SpecialType.System_Double => $"writer.WriteDouble({itemVar})",
                SpecialType.System_Boolean => $"writer.WriteByte((byte)({itemVar} ? 1 : 0))",
                SpecialType.System_String => $"writer.WriteString({itemVar})",
                _ => throw new NotSupportedException($"Element type {elementType.Name} is not supported")
            };
            
            sb.Append(writeMethod);
        }

        internal static string GetSafeTypeName(ITypeSymbol type)
        {
            // Create a safe method name from the type
            var name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            name = name.Replace(".", "_").Replace("<", "_").Replace(">", "_").Replace("[", "_").Replace("]", "_").Replace(",", "_").Replace(" ", "").Replace("?", "");
            return name;
        }

    }
}
