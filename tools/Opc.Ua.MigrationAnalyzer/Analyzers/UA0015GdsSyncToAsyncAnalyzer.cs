// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0015: Detect calls to obsolete synchronous / APM members on the
    /// OPC UA discovery clients (<c>GlobalDiscoveryServerClient</c>,
    /// <c>ServerPushConfigurationClient</c>, <c>LocalDiscoveryServerClient</c>)
    /// and recommend the async counterparts.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0015GdsSyncToAsyncAnalyzer : DiagnosticAnalyzer
    {
        private static readonly string[] s_targetTypeNames =
        [
            "Opc.Ua.GlobalDiscoveryServerClient",
            "Opc.Ua.ServerPushConfigurationClient",
            "Opc.Ua.LocalDiscoveryServerClient"
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0015_GdsSyncToAsync];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            HashSet<INamedTypeSymbol> targets = new(SymbolEqualityComparer.Default);
            foreach (string name in s_targetTypeNames)
            {
                INamedTypeSymbol sym = context.Compilation.GetTypeByMetadataName(name);
                if (sym != null)
                {
                    targets.Add(sym);
                }
            }

            context.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, targets),
                OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(
            OperationAnalysisContext context,
            HashSet<INamedTypeSymbol> targets)
        {
            var invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;
            if (method is null)
            {
                return;
            }

            bool isShim = method.IsOpcUaShim("UA0015");
            if (!isShim)
            {
                INamedTypeSymbol containing = method.ContainingType;
                if (containing is null || !targets.Contains(containing))
                {
                    return;
                }

                if (!method.IsObsolete())
                {
                    return;
                }
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0015_GdsSyncToAsync,
                invocation.Syntax.GetLocation(),
                method.Name));
        }
    }
}
