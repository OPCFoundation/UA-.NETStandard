/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
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
    /// UA0004: Detect null-conditional access (<c>?.</c>) whose receiver is
    /// one of the now-struct OPC UA built-in types. Since the receiver can
    /// never be <c>null</c>, the <c>?.</c> is misleading.
    /// </summary>
    /// <remarks>
    /// Implemented as a syntax-node action so the analyzer still fires on
    /// migration code that no longer compiles (e.g. <c>nodeId?.X</c> where
    /// <c>NodeId</c> is now a non-nullable struct).
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0004ConditionalAccessOnStructAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0004_ConditionalAccessOnStructType];

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
            if (!symbols.ReferencesOpcUa)
            {
                return;
            }
            context.RegisterSyntaxNodeAction(
                c => AnalyzeConditionalAccess(c, symbols),
                SyntaxKind.ConditionalAccessExpression);
        }

        private static void AnalyzeConditionalAccess(SyntaxNodeAnalysisContext context, UaSymbols symbols)
        {
            var node = (ConditionalAccessExpressionSyntax)context.Node;
            ITypeSymbol receiverType = context.SemanticModel
                .GetTypeInfo(node.Expression, context.CancellationToken).Type;
            if (receiverType is null)
            {
                return;
            }

            if (!symbols.IsBuiltInStructType(receiverType))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0004_ConditionalAccessOnStructType,
                node.GetLocation(),
                receiverType.Name));
        }
    }
}
