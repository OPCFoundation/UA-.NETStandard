// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0025: Detect use of <c>ILocalNode.DataLock</c> (and its <c>Node.DataLock</c>
    /// implementation), which 1.5.378 exposed and 2.0 removed.
    /// </summary>
    /// <remarks>
    /// The property returned the node instance itself, so every caller that took it shared
    /// one lock with the stack and with every other caller, in an order none of them could
    /// see. The member is gone in 2.0, so a consumer compiled against the new assemblies
    /// sees CS1061 rather than this diagnostic. The rule earns its keep on the migration
    /// path, where the analyzer package runs against sources still written for 1.5.378.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0025RemovedNodeDataLockAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0025_RemovedNodeDataLock];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Syntax rather than symbols: on the migration path the member no longer
            // resolves, so there is no symbol left to match on.
            context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;

            if (memberAccess.Name.Identifier.ValueText != "DataLock")
            {
                return;
            }

            ITypeSymbol? type = context.SemanticModel
                .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

            if (type is null || !TypeNames.IsLocalNode(type))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UA0025_RemovedNodeDataLock,
                    memberAccess.GetLocation(),
                    type.Name));
        }
    }
}
