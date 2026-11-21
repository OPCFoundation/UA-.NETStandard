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
    /// UA0026: Detect use of <c>BaseVariableValue.Lock</c>, which 1.5.378 exposed as an
    /// <c>object</c> and 2.0 removed.
    /// </summary>
    /// <remarks>
    /// The value owns its lock and no longer hands it out, so a caller cannot hold it
    /// across a call back into the stack. Code that has to make its own state atomic with
    /// the value constructs the value with a lock it already owns and takes that instead.
    /// The member is gone in 2.0, so a consumer compiled against the new assemblies sees
    /// CS1061 rather than this diagnostic. The rule earns its keep on the migration path,
    /// where the analyzer package runs against sources still written for 1.5.378.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0026RemovedVariableValueLockAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0026_RemovedVariableValueLock];

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

            if (memberAccess.Name.Identifier.ValueText != "Lock")
            {
                return;
            }

            ITypeSymbol? type = context.SemanticModel
                .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

            if (type is null || !TypeNames.IsBaseVariableValue(type))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UA0026_RemovedVariableValueLock,
                    memberAccess.GetLocation(),
                    type.Name));
        }
    }
}
