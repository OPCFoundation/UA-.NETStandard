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
    /// UA0007: Detect <c>new NodeId(string)</c> / <c>new ExpandedNodeId(string)</c>
    /// constructor calls and recommend the explicit <c>Parse</c> factory.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0007ObsoleteNodeIdStringCtorAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0007_ObsoleteNodeIdStringCtor];

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
            if (ctor is null || ctor.Parameters.Length != 1)
            {
                return;
            }

            INamedTypeSymbol containing = ctor.ContainingType;
            if (containing is null)
            {
                return;
            }
            string containingName = containing.ToDisplayString();
            if (containingName is not "Opc.Ua.NodeId" and not "Opc.Ua.ExpandedNodeId")
            {
                return;
            }

            if (ctor.Parameters[0].Type?.SpecialType != SpecialType.System_String)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0007_ObsoleteNodeIdStringCtor,
                creation.Syntax.GetLocation(),
                containing.Name));
        }
    }
}
