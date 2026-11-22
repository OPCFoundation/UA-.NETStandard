// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Prevents additional partial state from invalidating generated ECM ownership copies.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EcmFieldCopyEligibilityAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc/>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            [EcmFieldCopyGeneration.InvalidShape];

        /// <inheritdoc/>
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(
                GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.RegisterCompilationAction(static analysis =>
            {
                if (!EcmFieldCopyGeneration.IsEnabled(analysis.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
                {
                    return;
                }
                foreach ((string name, ImmutableArray<string> fields) in EcmFieldCopyGeneration.Shapes)
                {
                    INamedTypeSymbol? type = analysis.Compilation.GetTypeByMetadataName("Opc.Ua.ECM." + name);
                    if (type == null ||
                        !SymbolEqualityComparer.Default.Equals(
                            type.ContainingAssembly, analysis.Compilation.Assembly) ||
                        !EcmFieldCopyGeneration.IsEligible(type, fields))
                    {
                        analysis.ReportDiagnostic(Diagnostic.Create(
                            EcmFieldCopyGeneration.InvalidShape, type?.Locations.FirstOrDefault() ?? Location.None,
                            name + " requires only its generated data and pooling declarations, " +
                            "no added state or base class, " +
                            "and only its three nonvirtual scalar Float properties and backing fields."));
                    }
                }
            });
        }
    }
}
