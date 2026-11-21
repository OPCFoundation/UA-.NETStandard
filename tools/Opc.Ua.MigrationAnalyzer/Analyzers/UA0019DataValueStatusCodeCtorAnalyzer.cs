// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0019: Detect <c>new DataValue(StatusCode)</c> and
    /// <c>new DataValue(StatusCode, DateTimeUtc)</c> constructor calls and
    /// recommend the explicit <c>DataValue.FromStatusCode(...)</c> factory.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0019DataValueStatusCodeCtorAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0019_ObsoleteDataValueStatusCodeCtor];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        }

        private static void AnalyzeObjectCreation(OperationAnalysisContext context)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            IMethodSymbol ctor = creation.Constructor;
            if (ctor is null)
            {
                return;
            }

            INamedTypeSymbol containing = ctor.ContainingType;
            if (containing is null || containing.ToDisplayString() != "Opc.Ua.DataValue")
            {
                return;
            }

            if (ctor.Parameters.Length is < 1 or > 2)
            {
                return;
            }

            if (ctor.Parameters[0].Type?.ToDisplayString() != "Opc.Ua.StatusCode")
            {
                return;
            }

            string extra = string.Empty;
            if (ctor.Parameters.Length == 2)
            {
                if (ctor.Parameters[1].Type?.ToDisplayString() != "Opc.Ua.DateTimeUtc")
                {
                    return;
                }
                extra = ", DateTimeUtc";
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0019_ObsoleteDataValueStatusCodeCtor,
                creation.Syntax.GetLocation(),
                extra));
        }
    }
}
