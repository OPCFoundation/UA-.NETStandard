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

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0027: Detect use of <c>NodeBrowser.DataLock</c>, the protected synchronization lock
    /// that 1.5.378 exposed to derived browsers, and recommend removing it.
    /// </summary>
    /// <remarks>
    /// Matched on syntax rather than on symbols, for the same reason as
    /// <see cref="UA0024RemovedDiagnosticsLockAnalyzer"/>: on the migration path the member no
    /// longer resolves, so there is no symbol left to bind to. A derived browser referring to
    /// <c>DataLock</c> without a qualifier is the common shape, so the rule also matches a bare
    /// identifier inside a type that derives from <c>NodeBrowser</c>. Where the name does bind
    /// - a local, parameter or unrelated member that happens to be called <c>DataLock</c> - the
    /// resolved symbol is used to reject it, so the syntax match is a fallback rather than the
    /// whole rule.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0027RemovedNodeBrowserDataLockAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0027_RemovedNodeBrowserDataLock];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(
                AnalyzeMemberAccess,
                SyntaxKind.SimpleMemberAccessExpression);
            context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
        }

        private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccess = (MemberAccessExpressionSyntax)context.Node;

            if (memberAccess.Name.Identifier.ValueText != DataLockName)
            {
                return;
            }

            if (BindsToSomethingElse(context, memberAccess.Name))
            {
                return;
            }

            ITypeSymbol? type = context.SemanticModel
                .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

            if (type is null || !DerivesFromNodeBrowser(context, type))
            {
                return;
            }

            Report(context, memberAccess.GetLocation());
        }

        private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
        {
            var identifier = (IdentifierNameSyntax)context.Node;

            if (identifier.Identifier.ValueText != DataLockName)
            {
                return;
            }

            // The qualified form is handled by AnalyzeMemberAccess; reporting the name part of
            // a member access here as well would produce two diagnostics for one expression.
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name == identifier)
            {
                return;
            }

            if (BindsToSomethingElse(context, identifier))
            {
                return;
            }

            INamedTypeSymbol? containingType = context.SemanticModel
                .GetEnclosingSymbol(identifier.SpanStart, context.CancellationToken)?
                .ContainingType;

            if (containingType is null || !DerivesFromNodeBrowser(context, containingType))
            {
                return;
            }

            Report(context, identifier.GetLocation());
        }

        /// <summary>
        /// True when the name binds to something that is demonstrably not the removed member -
        /// a local, a parameter, a type, or a member declared by a type that is not a
        /// <c>NodeBrowser</c>. An unresolved name is not rejected: that is the migration case
        /// the rule exists for.
        /// </summary>
        private static bool BindsToSomethingElse(
            SyntaxNodeAnalysisContext context,
            SimpleNameSyntax name)
        {
            ISymbol? symbol = context.SemanticModel
                .GetSymbolInfo(name, context.CancellationToken).Symbol;

            if (symbol is null)
            {
                return false;
            }

            if (symbol is ILocalSymbol or IParameterSymbol or ITypeSymbol or INamespaceSymbol)
            {
                return true;
            }

            INamedTypeSymbol? owner = symbol.ContainingType;

            return owner is null || !DerivesFromNodeBrowser(context, owner);
        }

        /// <summary>
        /// True when the type is <c>Opc.Ua.NodeBrowser</c> or derives from it.
        /// </summary>
        /// <remarks>
        /// Falls back to a name comparison when the compilation has no
        /// <c>Opc.Ua.NodeBrowser</c> - the analyzer package runs against sources that may
        /// declare their own shim while the real reference is being swapped over.
        /// </remarks>
        private static bool DerivesFromNodeBrowser(
            SyntaxNodeAnalysisContext context,
            ITypeSymbol type)
        {
            INamedTypeSymbol? nodeBrowser = context.Compilation
                .GetTypeByMetadataName(NodeBrowserMetadataName);

            for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (nodeBrowser is not null)
                {
                    if (SymbolEqualityComparer.Default.Equals(current, nodeBrowser))
                    {
                        return true;
                    }
                }
                else if (current.Name == NodeBrowserName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Report(SyntaxNodeAnalysisContext context, Location location)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.UA0027_RemovedNodeBrowserDataLock,
                    location));
        }

        private const string DataLockName = "DataLock";
        private const string NodeBrowserName = "NodeBrowser";
        private const string NodeBrowserMetadataName = "Opc.Ua.NodeBrowser";
    }
}
