/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0002: Detect references to removed <c>&lt;Type&gt;Collection</c>
    /// wrappers (Int32Collection, NodeIdCollection, ...) and recommend
    /// <c>List&lt;T&gt;</c> or <c>ArrayOf&lt;T&gt;</c>.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0002RemovedCollectionTypeAnalyzer : DiagnosticAnalyzer
    {
        public const string ElementTypeProperty = "ElementType";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0002_RemovedCollectionType);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
        }

        private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
        {
            IdentifierNameSyntax id = (IdentifierNameSyntax)context.Node;

            SymbolInfo info = context.SemanticModel.GetSymbolInfo(id, context.CancellationToken);
            if (info.Symbol is not INamedTypeSymbol named)
            {
                return;
            }

            if (!named.TryGetRemovedCollectionElement(out string elementName))
            {
                return;
            }

            ImmutableDictionary<string, string> properties = ImmutableDictionary<string, string>.Empty
                .Add(ElementTypeProperty, TrimOpcUaPrefix(elementName));

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0002_RemovedCollectionType,
                id.GetLocation(),
                properties,
                named.ToDisplayString(),
                TrimOpcUaPrefix(elementName)));
        }

        internal static string TrimOpcUaPrefix(string name)
        {
            const string prefix = "Opc.Ua.";
            if (name != null && name.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return name.Substring(prefix.Length);
            }
            return name;
        }
    }
}
