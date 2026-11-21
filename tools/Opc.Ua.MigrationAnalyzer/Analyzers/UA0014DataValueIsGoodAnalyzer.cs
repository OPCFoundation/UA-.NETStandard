// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0014: Replace the obsolete static <c>DataValue.IsGood(dv)</c> /
    /// <c>IsBad</c> / <c>IsUncertain</c> (+ <c>IsNotXxx</c>) helpers with the
    /// matching instance property on <c>DataValue</c>.
    /// </summary>
    /// <remarks>
    /// Detection: any <see cref="IInvocationOperation"/> that targets a static
    /// method named one of the six helpers on either <c>Opc.Ua.DataValue</c> or
    /// <c>Opc.Ua.DataValueExtensions</c>, with a single <c>DataValue</c> argument.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0014DataValueIsGoodAnalyzer : DiagnosticAnalyzer
    {
        private static readonly HashSet<string> s_targetNames =
        [
            "IsGood",
            "IsBad",
            "IsUncertain",
            "IsNotGood",
            "IsNotBad",
            "IsNotUncertain"
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0014_DataValueIsGoodStaticToInstance];

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

            if (!method.IsStatic ||
                method.Parameters.Length != 1 ||
                !s_targetNames.Contains(method.Name))
            {
                return;
            }

            INamedTypeSymbol containing = method.ContainingType;
            if (containing is null)
            {
                return;
            }
            string containingName = containing.ToDisplayString();
            if (containingName is not "Opc.Ua.DataValue" and
                not "Opc.Ua.DataValueExtensions")
            {
                return;
            }

            ITypeSymbol argType = method.Parameters[0].Type;
            if (argType is null || argType.ToDisplayString() != "Opc.Ua.DataValue")
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0014_DataValueIsGoodStaticToInstance,
                invocation.Syntax.GetLocation(),
                method.Name));
        }
    }
}
