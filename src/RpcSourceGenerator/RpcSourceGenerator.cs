using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace FrameWork.NetWork.SourceGenerators
{
    [Generator]
    public class RpcSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var handlerClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, _) => GetHandlerClassOrNull(ctx))
                .Where(static m => m is not null);

            var compilationAndClasses = context.CompilationProvider.Combine(handlerClasses.Collect());

            context.RegisterSourceOutput(compilationAndClasses,
                static (spc, source) => Execute(source.Left, source.Right, spc));
        }

        private static ClassDeclarationSyntax GetHandlerClassOrNull(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;

            if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return null;

            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
            if (symbol == null)
                return null;

            var baseType = symbol.BaseType;
            while (baseType != null)
            {
                if (baseType.Name == "PacketHandler" &&
                    baseType.ContainingNamespace?.ToString() == "FrameWork.NetWork.V4")
                    return classDeclaration;
                baseType = baseType.BaseType;
            }

            return null;
        }

        private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax> classes,
            SourceProductionContext context)
        {
            if (classes.IsDefaultOrEmpty)
                return;

            foreach (var classDeclaration in classes)
            {
                var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
                if (classSymbol == null)
                    continue;

                var rpcMethods = new List<RpcMethodInfo>();
                var opcodes = new HashSet<byte>();

                foreach (var member in classSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    var rpcAttribute = member.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "RpcAttribute" &&
                                             a.AttributeClass.ContainingNamespace?.ToString() ==
                                             "FrameWork.NetWork.V4");

                    if (rpcAttribute == null)
                        continue;

                    if (rpcAttribute.ConstructorArguments.Length == 0)
                        continue;

                    var opcodeValue = rpcAttribute.ConstructorArguments[0].Value;
                    if (opcodeValue == null)
                        continue;

                    byte opcode = (byte)opcodeValue;

                    byte responseOpcode = opcode;
                    if (rpcAttribute.ConstructorArguments.Length > 1)
                    {
                        var responseOpcodeValue = rpcAttribute.ConstructorArguments[1].Value;
                        if (responseOpcodeValue != null)
                            responseOpcode = (byte)responseOpcodeValue;
                    }

                    // Check for duplicate opcodes
                    if (!opcodes.Add(opcode))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "RPC001",
                                "Duplicate opcode",
                                $"Opcode 0x{opcode:X2} is already used by another handler in class {classSymbol.Name}",
                                "RpcGenerator",
                                DiagnosticSeverity.Error,
                                isEnabledByDefault: true),
                            member.Locations.FirstOrDefault()));
                        continue;
                    }

                    // Classify parameters
                    var parameters = new List<RpcParameterInfo>();
                    RpcParameterInfo requestParam = null;
                    bool hasValidationError = false;

                    foreach (var param in member.Parameters)
                    {
                        // Check for [FromServices]
                        var fromServicesAttr = param.GetAttributes()
                            .FirstOrDefault(a =>
                                a.AttributeClass?.Name == "FromServicesAttribute" &&
                                a.AttributeClass.ContainingNamespace?.ToString() == "FrameWork.NetWork.V4");

                        if (fromServicesAttr != null)
                        {
                            parameters.Add(new RpcParameterInfo
                            {
                                Kind = ParameterKind.Service,
                                TypeName = param.Type.ToDisplayString(),
                                ParameterName = param.Name
                            });
                            continue;
                        }

                        // Check for IConnectionContext
                        if (param.Type.Name == "IConnectionContext" &&
                            param.Type.ContainingNamespace?.ToString() == "FrameWork.NetWork.V4")
                        {
                            parameters.Add(new RpcParameterInfo
                            {
                                Kind = ParameterKind.Context,
                                TypeName = param.Type.ToDisplayString(),
                                ParameterName = param.Name
                            });
                            continue;
                        }

                        // Request DTO parameter
                        if (requestParam != null)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                new DiagnosticDescriptor(
                                    "RPC002",
                                    "Invalid RPC handler signature",
                                    $"RPC handler '{member.Name}' must have at most one request parameter (non-[FromServices], non-IConnectionContext)",
                                    "RpcGenerator",
                                    DiagnosticSeverity.Error,
                                    isEnabledByDefault: true),
                                member.Locations.FirstOrDefault()));
                            hasValidationError = true;
                            break;
                        }

                        requestParam = new RpcParameterInfo
                        {
                            Kind = ParameterKind.Request,
                            TypeName = param.Type.ToDisplayString(),
                            ParameterName = param.Name
                        };
                        parameters.Add(requestParam);
                    }

                    if (hasValidationError)
                        continue;

                    // Determine return type
                    var returnType = member.ReturnType;
                    bool isAsync = returnType.Name == "Task" || returnType.Name == "ValueTask";
                    bool hasResponse = false;
                    ITypeSymbol responseType = null;

                    if (isAsync && returnType is INamedTypeSymbol namedReturnType &&
                        namedReturnType.TypeArguments.Length > 0)
                    {
                        hasResponse = true;
                        responseType = namedReturnType.TypeArguments[0];
                    }
                    else if (!isAsync && returnType.SpecialType != SpecialType.System_Void)
                    {
                        hasResponse = true;
                        responseType = returnType;
                    }

                    rpcMethods.Add(new RpcMethodInfo
                    {
                        MethodName = member.Name,
                        Opcode = opcode,
                        ResponseOpcode = responseOpcode,
                        ResponseType = responseType?.ToDisplayString(),
                        HasResponse = hasResponse,
                        IsAsync = isAsync,
                        Parameters = parameters,
                        HasServices = parameters.Any(p => p.Kind == ParameterKind.Service)
                    });
                }

                if (rpcMethods.Count > 0)
                {
                    var source = GenerateSource(classSymbol, rpcMethods);
                    context.AddSource($"{classSymbol.Name}_RpcGenerated.g.cs", source);
                }
            }
        }

        private static string GenerateSource(INamedTypeSymbol classSymbol, List<RpcMethodInfo> methods)
        {
            var namespaceName = classSymbol.ContainingNamespace?.ToDisplayString();
            var className = classSymbol.Name;
            bool anyServices = methods.Any(m => m.HasServices);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using FrameWork.NetWork.V4;");
            if (anyServices)
                sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    partial class {className}");
            sb.AppendLine("    {");

            // Generate the Dispatcher nested class
            sb.AppendLine($"        public sealed class Dispatcher : IPacketDispatcher<{className}>");
            sb.AppendLine("        {");
            sb.AppendLine($"            public void Dispatch(");
            sb.AppendLine($"                {className} handler,");
            sb.AppendLine("                byte opcode,");
            sb.AppendLine("                ReadOnlyMemory<byte> payload,");
            sb.AppendLine("                IServiceProvider services,");
            sb.AppendLine("                IPacketSerializer serializer,");
            sb.AppendLine("                IConnectionContext connection)");
            sb.AppendLine("            {");
            sb.AppendLine("                switch (opcode)");
            sb.AppendLine("                {");

            foreach (var method in methods.OrderBy(m => m.Opcode))
            {
                GenerateSwitchCase(sb, className, method);
            }

            sb.AppendLine("                    default:");
            sb.AppendLine("                        break;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");

            // Generate async wrapper methods
            foreach (var method in methods.Where(m => m.IsAsync).OrderBy(m => m.Opcode))
            {
                GenerateAsyncWrapper(sb, className, method);
            }

            sb.AppendLine("        }"); // end Dispatcher class
            sb.AppendLine("    }"); // end partial class

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static void GenerateSwitchCase(StringBuilder sb, string className, RpcMethodInfo method)
        {
            sb.AppendLine($"                    case 0x{method.Opcode:X2}:");
            sb.AppendLine("                    {");

            var requestParam = method.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.Request);
            var serviceParams = method.Parameters.Where(p => p.Kind == ParameterKind.Service).ToList();

            // Deserialize request if present
            if (requestParam != null)
            {
                sb.AppendLine(
                    $"                        var request = serializer.Deserialize<{requestParam.TypeName}>(payload.Span);");
            }

            // Create scope for services if needed
            bool needsScope = serviceParams.Count > 0;
            if (needsScope)
            {
                if (method.IsAsync)
                {
                    // For async: scope is passed to wrapper and disposed there
                    sb.AppendLine(
                        "                        var __scope = services.CreateScope();");
                }
                else
                {
                    // For sync: scope is disposed at end of case block
                    sb.AppendLine(
                        "                        using var __scope = services.CreateScope();");
                }

                // Resolve each service
                foreach (var svc in serviceParams)
                {
                    sb.AppendLine(
                        $"                        var __svc_{svc.ParameterName} = __scope.ServiceProvider.GetRequiredService<{svc.TypeName}>();");
                }
            }

            // Build argument list in declaration order
            var args = BuildArgumentList(method.Parameters);

            if (method.IsAsync)
            {
                // Build async wrapper argument list
                var wrapperArgs = new List<string> { "handler" };
                if (requestParam != null) wrapperArgs.Add("request");
                foreach (var svc in serviceParams) wrapperArgs.Add($"__svc_{svc.ParameterName}");
                wrapperArgs.Add("connection");
                if (needsScope) wrapperArgs.Add("__scope");

                sb.AppendLine(
                    $"                        _ = DispatchAsync_{method.MethodName}({string.Join(", ", wrapperArgs)});");
            }
            else
            {
                if (method.HasResponse)
                {
                    sb.AppendLine($"                        var response = handler.{method.MethodName}({args});");
                    sb.AppendLine($"                        if (response != null)");
                    sb.AppendLine(
                        $"                            connection.SendResponse(0x{method.ResponseOpcode:X2}, response);");
                }
                else
                {
                    sb.AppendLine($"                        handler.{method.MethodName}({args});");
                }
            }

            sb.AppendLine("                        break;");
            sb.AppendLine("                    }");
        }

        private static void GenerateAsyncWrapper(StringBuilder sb, string className, RpcMethodInfo method)
        {
            var requestParam = method.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.Request);
            var serviceParams = method.Parameters.Where(p => p.Kind == ParameterKind.Service).ToList();
            bool needsScope = serviceParams.Count > 0;

            sb.AppendLine();

            // Build parameters for the wrapper method
            var wrapperParams = new List<string> { $"{className} handler" };
            if (requestParam != null)
                wrapperParams.Add($"{requestParam.TypeName} request");
            foreach (var svc in serviceParams)
                wrapperParams.Add($"{svc.TypeName} __svc_{svc.ParameterName}");
            wrapperParams.Add("IConnectionContext connection");
            if (needsScope)
                wrapperParams.Add("IServiceScope __scope");

            sb.AppendLine(
                $"            private static async Task DispatchAsync_{method.MethodName}({string.Join(", ", wrapperParams)})");
            sb.AppendLine("            {");
            sb.AppendLine("                try");
            sb.AppendLine("                {");

            // Build handler call argument list in declaration order
            var args = BuildArgumentList(method.Parameters);

            if (method.HasResponse)
            {
                sb.AppendLine($"                    var response = await handler.{method.MethodName}({args});");
                sb.AppendLine($"                    if (response != null)");
                sb.AppendLine(
                    $"                        connection.SendResponse(0x{method.ResponseOpcode:X2}, response);");
            }
            else
            {
                sb.AppendLine($"                    await handler.{method.MethodName}({args});");
            }

            sb.AppendLine("                }");
            sb.AppendLine("                catch (Exception ex)");
            sb.AppendLine("                {");
            sb.AppendLine($"                    connection.OnDispatchError(0x{method.Opcode:X2}, ex);");
            sb.AppendLine("                }");

            if (needsScope)
            {
                sb.AppendLine("                finally");
                sb.AppendLine("                {");
                sb.AppendLine("                    __scope.Dispose();");
                sb.AppendLine("                }");
            }

            sb.AppendLine("            }");
        }

        private static string BuildArgumentList(List<RpcParameterInfo> parameters)
        {
            var args = new List<string>();
            foreach (var param in parameters)
            {
                switch (param.Kind)
                {
                    case ParameterKind.Request:
                        args.Add("request");
                        break;
                    case ParameterKind.Context:
                        args.Add("connection");
                        break;
                    case ParameterKind.Service:
                        args.Add($"__svc_{param.ParameterName}");
                        break;
                }
            }

            return string.Join(", ", args);
        }

        private enum ParameterKind
        {
            Request,
            Context,
            Service
        }

        private class RpcParameterInfo
        {
            public ParameterKind Kind { get; set; }
            public string TypeName { get; set; }
            public string ParameterName { get; set; }
        }

        private class RpcMethodInfo
        {
            public string MethodName { get; set; }
            public byte Opcode { get; set; }
            public byte ResponseOpcode { get; set; }
            public string ResponseType { get; set; }
            public bool HasResponse { get; set; }
            public bool IsAsync { get; set; }
            public List<RpcParameterInfo> Parameters { get; set; }
            public bool HasServices { get; set; }
        }
    }
}
