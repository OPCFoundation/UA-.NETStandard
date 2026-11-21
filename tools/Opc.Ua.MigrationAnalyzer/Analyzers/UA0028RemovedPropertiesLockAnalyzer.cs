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
    /// UA0028: Detect use of <c>ApplicationConfiguration.PropertiesLock</c>, which 1.5.378
    /// exposed and 2.0 removed.
    /// </summary>
    /// <remarks>
    /// The property returned the properties dictionary itself, so the lock was the data it
    /// guarded. The member is gone in 2.0, so a consumer compiled against the new assemblies
    /// sees CS1061 rather than this diagnostic. Like
    /// <see cref="UA0025RemovedNodeDataLockAnalyzer"/>, the rule earns its keep on the
    /// migration path, where the analyzer package runs against sources still written for
    /// 1.5.378 and the member still resolves.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0028RemovedPropertiesLockAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0028_RemovedPropertiesLock];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Syntax rather than symbols: on the migration path the member no longer
            // resolves, so there is no symbol left to match on.
            context.RegisterSyntaxNodeAction(
                AnalyzeMemberAccess,
                SyntaxKind.SimpleMemberAccessExpression);
        }

        private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;

            if (memberAccess.Name.Identifier.ValueText != "PropertiesLock")
            {
                return;
            }

            ITypeSymbol? type = context.SemanticModel
                .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

            // The type is unresolvable on the migration path when the member has already
            // gone, so an unknown type is reported rather than skipped: the name is
            // distinctive enough that a false positive is unlikely.
            if (type is not null &&
                !TypeNames.DerivesFrom(type, "ApplicationConfiguration") &&
                type.Name != "ApplicationConfiguration")
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UA0028_RemovedPropertiesLock,
                    memberAccess.GetLocation()));
        }
    }
}
