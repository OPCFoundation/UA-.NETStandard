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

using System;
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
    /// UA0021: Detect references to the legacy <c>Opc.Ua.CertificateValidator</c> class and
    /// <c>Opc.Ua.CertificateValidationEventArgs</c>, which were removed in 1.6 in favour of the
    /// new <c>ICertificateManager</c> / <c>ICertificateValidatorEx</c> /
    /// <c>CertificateValidationResult</c> pipeline.
    /// </summary>
    /// <remarks>
    /// Diagnostic-only: the 1.5.378 -> 1.6 change is <b>structural</b> (event-based per-error
    /// accept handler became an async <c>ValidateAsync</c> call returning a
    /// <c>CertificateValidationResult</c>, with per-error accept logic moving to
    /// <c>CertificateValidationOptions.AcceptError</c>). There is therefore no accompanying
    /// code-fix provider; consumers must perform the migration manually using
    /// <c>Docs/MigrationGuide.md#certificatemanager-and-segregated-interfaces</c>.
    ///
    /// Detection strategy:
    /// <list type="bullet">
    /// <item>If the legacy type is present in the compilation (via the 1.5.378 stack or the shim
    /// package), fire when an <c>[Obsolete]</c>-marked type with the matching full name is
    /// referenced.</item>
    /// <item>If the legacy type has been genuinely removed (consumer is on the bare 1.6 stack
    /// and the call site no longer compiles), fall back to a syntactic match on the bare
    /// identifier name, scoped to source files that import any <c>Opc.Ua</c> namespace (bare
    /// <c>using Opc.Ua;</c> or any sub-namespace such as <c>using Opc.Ua.Server;</c>) or that
    /// declare a namespace under the <c>Opc.Ua</c> tree.</item>
    /// </list>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0021CertificateValidatorRenameAnalyzer : DiagnosticAnalyzer
    {
        private const string OpcUaNamespace = "Opc.Ua";
        private const string CertificateValidatorTypeName = "CertificateValidator";
        private const string CertificateValidationEventArgsTypeName = "CertificateValidationEventArgs";
        private const string CertificateValidatorFullName = OpcUaNamespace + "." + CertificateValidatorTypeName;
        private const string CertificateValidationEventArgsFullName =
            OpcUaNamespace + "." + CertificateValidationEventArgsTypeName;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(DiagnosticDescriptors.UA0021_CertificateValidatorRename);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(static startContext =>
            {
                INamedTypeSymbol? legacyValidator =
                    startContext.Compilation.GetTypeByMetadataName(CertificateValidatorFullName);
                INamedTypeSymbol? legacyEventArgs =
                    startContext.Compilation.GetTypeByMetadataName(CertificateValidationEventArgsFullName);

                bool hasLegacySymbol = legacyValidator is not null || legacyEventArgs is not null;

                if (hasLegacySymbol)
                {
                    startContext.RegisterSyntaxNodeAction(
                        ctx => AnalyzeIdentifierSymbol(ctx, legacyValidator, legacyEventArgs),
                        SyntaxKind.IdentifierName);
                }
                else
                {
                    startContext.RegisterSyntaxNodeAction(
                        AnalyzeIdentifierSyntactic,
                        SyntaxKind.IdentifierName);
                }
            });
        }

        private static void AnalyzeIdentifierSymbol(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol? legacyValidator,
            INamedTypeSymbol? legacyEventArgs)
        {
            IdentifierNameSyntax identifier = (IdentifierNameSyntax)context.Node;
            string text = identifier.Identifier.ValueText;
            if (text != CertificateValidatorTypeName && text != CertificateValidationEventArgsTypeName)
            {
                return;
            }

            SymbolInfo info = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken);
            ISymbol? symbol = info.Symbol;
            if (symbol is null)
            {
                return;
            }

            INamedTypeSymbol? namedType = symbol as INamedTypeSymbol
                ?? (symbol as IMethodSymbol)?.ContainingType;
            if (namedType is null)
            {
                return;
            }

            bool matchesLegacy =
                (legacyValidator is not null
                    && SymbolEqualityComparer.Default.Equals(namedType, legacyValidator)) ||
                (legacyEventArgs is not null
                    && SymbolEqualityComparer.Default.Equals(namedType, legacyEventArgs));
            if (!matchesLegacy)
            {
                return;
            }

            if (!namedType.IsObsolete())
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0021_CertificateValidatorRename,
                identifier.GetLocation(),
                namedType.Name));
        }

        private static void AnalyzeIdentifierSyntactic(SyntaxNodeAnalysisContext context)
        {
            IdentifierNameSyntax identifier = (IdentifierNameSyntax)context.Node;
            string text = identifier.Identifier.ValueText;
            if (text != CertificateValidatorTypeName && text != CertificateValidationEventArgsTypeName)
            {
                return;
            }

            if (!HasOpcUaContext(identifier))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0021_CertificateValidatorRename,
                identifier.GetLocation(),
                text));
        }

        private static bool HasOpcUaContext(SyntaxNode node)
        {
            for (SyntaxNode? current = node; current is not null; current = current.Parent)
            {
                if (current is BaseNamespaceDeclarationSyntax ns)
                {
                    if (IsOpcUaNamespaceName(ns.Name) || ContainsOpcUaUsing(ns.Usings))
                    {
                        return true;
                    }
                }

                if (current is CompilationUnitSyntax compilationUnit)
                {
                    return ContainsOpcUaUsing(compilationUnit.Usings);
                }
            }

            return false;
        }

        private static bool ContainsOpcUaUsing(SyntaxList<UsingDirectiveSyntax> usings)
        {
            foreach (UsingDirectiveSyntax @using in usings)
            {
                if (@using.Alias is not null || @using.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                {
                    continue;
                }
                if (@using.Name is null)
                {
                    continue;
                }
                if (IsOpcUaNamespaceName(@using.Name))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsOpcUaNamespaceName(NameSyntax? name)
        {
            if (name is null)
            {
                return false;
            }
            string text = name.ToString();
            // Match bare "Opc.Ua" or any sub-namespace such as "Opc.Ua.Server",
            // "Opc.Ua.Configuration", "Opc.Ua.Client.ComplexTypes" etc. We require a '.'
            // separator after "Opc.Ua" so that a hypothetical sibling namespace like
            // "Opc.UaFoo" does not match.
            return string.Equals(text, OpcUaNamespace, StringComparison.Ordinal)
                || text.StartsWith(OpcUaNamespace + ".", StringComparison.Ordinal);
        }
    }
}
