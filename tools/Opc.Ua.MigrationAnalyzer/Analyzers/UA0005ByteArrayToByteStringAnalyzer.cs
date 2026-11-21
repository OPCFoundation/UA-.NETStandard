// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0005: Detect call sites passing a <c>byte[]</c> argument where the
    /// resolved parameter type is now <c>Opc.Ua.ByteString</c>.
    /// </summary>
    /// <remarks>
    /// Implemented as a syntax-node action so the analyzer fires on migration
    /// code that no longer compiles (the <c>byte[]</c>-to-<c>ByteString</c>
    /// conversion is gone).
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0005ByteArrayToByteStringAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0005_ByteArrayWhereByteStringExpected];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            Dictionary<Compilation, UaSymbols> cache = [];
            var symbols = UaSymbols.For(context.Compilation, cache);
            if (!symbols.ReferencesOpcUa || symbols.ByteStringType is null)
            {
                return;
            }
            context.RegisterSyntaxNodeAction(
                c => AnalyzeInvocation(c, symbols),
                SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, UaSymbols symbols)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            SymbolInfo info = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
            IMethodSymbol method = info.Symbol as IMethodSymbol
                ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (method is null)
            {
                return;
            }

            INamedTypeSymbol byteStringType = symbols.ByteStringType;
            SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
            for (int i = 0; i < arguments.Count; i++)
            {
                ArgumentSyntax arg = arguments[i];
                IParameterSymbol parameter = i < method.Parameters.Length ? method.Parameters[i] : null;
                if (parameter is null)
                {
                    continue;
                }
                if (!SymbolEqualityComparer.Default.Equals(parameter.Type, byteStringType))
                {
                    continue;
                }

                ITypeSymbol valueType = context.SemanticModel
                    .GetTypeInfo(arg.Expression, context.CancellationToken).Type;
                if (valueType is not IArrayTypeSymbol array ||
                    array.ElementType.SpecialType != SpecialType.System_Byte)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UA0005_ByteArrayWhereByteStringExpected,
                    arg.GetLocation(),
                    method.Name));
            }
        }
    }
}
