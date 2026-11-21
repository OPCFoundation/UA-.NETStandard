// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// UA0001 is diagnostic-only. The replacement requires an ILogger instance
// obtained from ITelemetryContext.CreateLogger<T>() which the analyzer cannot
// synthesize automatically. A code fix would require the host type to expose
// an ILogger field or an ITelemetryContext from which to derive one.

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
    /// UA0001: Flag calls to the obsolete static <c>Opc.Ua.Utils.Trace</c> and
    /// <c>Opc.Ua.Utils.LogX</c> helpers. Replacement requires an
    /// <c>ILogger</c> from an <c>ITelemetryContext</c> which the analyzer
    /// cannot synthesize, so this rule ships diagnostic-only (no code fix).
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0001UtilsTraceToILoggerAnalyzer : DiagnosticAnalyzer
    {
        private static readonly HashSet<string> s_targetNames =
        [
            "Trace",
            "LogError",
            "LogWarning",
            "LogInformation",
            "LogDebug",
            "LogTrace",
            "LogCritical"
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0001_UtilsTraceToILogger];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        }

        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;

            if (!method.IsStatic || !s_targetNames.Contains(method.Name))
            {
                return;
            }

            INamedTypeSymbol containing = method.ContainingType;
            if (containing is null || containing.ToDisplayString() != "Opc.Ua.Utils")
            {
                return;
            }

            if (!method.IsObsolete())
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0001_UtilsTraceToILogger,
                invocation.Syntax.GetLocation(),
                "Utils." + method.Name));
        }
    }
}
