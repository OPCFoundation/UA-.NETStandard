/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

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
