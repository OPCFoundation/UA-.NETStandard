/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
            [DiagnosticDescriptors.UA0002_RemovedCollectionType];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
        }

        private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
        {
            var id = (IdentifierNameSyntax)context.Node;

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
