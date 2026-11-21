// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0008: Detect <c>ISession.Call</c> / <c>ISession.CallAsync</c>
    /// invocations whose variadic arguments are raw values instead of
    /// <c>Variant</c> instances, and recommend wrapping with
    /// <c>Variant.From(...)</c>.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0008SessionCallParamsObjectAnalyzer : DiagnosticAnalyzer
    {
        public const string MethodNameProperty = WellKnownProperties.MethodName;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0008_SessionCallParamsObject];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol sessionInterface = context.Compilation.GetTypeByMetadataName("Opc.Ua.ISession");
            INamedTypeSymbol variantType = context.Compilation.GetTypeByMetadataName("Opc.Ua.Variant");
            if (variantType is null)
            {
                return;
            }
            context.RegisterSyntaxNodeAction(
                c => AnalyzeInvocation(c, sessionInterface, variantType),
                SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol sessionInterface,
            INamedTypeSymbol variantType)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return;
            }

            string methodName = memberAccess.Name.Identifier.ValueText;
            if (methodName is not "Call" and not "CallAsync")
            {
                return;
            }

            var resolvedMethod = context.SemanticModel
                .GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;
            bool isShim = resolvedMethod.IsOpcUaShim("UA0008");

            if (!isShim)
            {
                if (sessionInterface is null)
                {
                    return;
                }
                ITypeSymbol receiverType = context.SemanticModel
                    .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
                if (receiverType is null || !receiverType.IsAssignableTo(sessionInterface))
                {
                    return;
                }
            }

            int firstVariadicIndex = methodName == "Call" ? 2 : 3;
            SeparatedSyntaxList<ArgumentSyntax> args = invocation.ArgumentList.Arguments;
            if (args.Count <= firstVariadicIndex)
            {
                return;
            }

            bool anyNonVariant = false;
            for (int i = firstVariadicIndex; i < args.Count; i++)
            {
                ITypeSymbol argType = context.SemanticModel
                    .GetTypeInfo(args[i].Expression, context.CancellationToken).Type;
                if (argType is null || !SymbolEqualityComparer.Default.Equals(argType, variantType))
                {
                    anyNonVariant = true;
                    break;
                }
            }

            if (!anyNonVariant)
            {
                return;
            }

            ImmutableDictionary<string, string> properties = ImmutableDictionary<string, string>.Empty
                .Add(MethodNameProperty, methodName);

            ITypeSymbol displayReceiver = context.SemanticModel
                .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
            string receiverName = displayReceiver?.Name ?? "ISession";
            string display = receiverName + "." + methodName;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0008_SessionCallParamsObject,
                invocation.GetLocation(),
                properties,
                display));
        }
    }
}
