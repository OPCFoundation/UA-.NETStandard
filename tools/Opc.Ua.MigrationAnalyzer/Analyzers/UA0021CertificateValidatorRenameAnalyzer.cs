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
    /// <c>Opc.Ua.CertificateValidationEventArgs</c>, which were removed in 2.0 in favour of the
    /// new <c>ICertificateManager</c> / <c>ICertificateValidatorEx</c> /
    /// <c>CertificateValidationResult</c> pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Diagnostic-only: the 1.5.378 -> 2.0 change is <b>structural</b> (event-based per-error
    /// accept handler became an async <c>ValidateAsync</c> call returning a
    /// <c>CertificateValidationResult</c>, with per-error accept logic moving to
    /// <c>CertificateValidationOptions.AcceptError</c>). There is therefore no accompanying
    /// code-fix provider; consumers must perform the migration manually using
    /// <c>docs/migrate/2.0.x/certificates.md#certificatemanager-and-segregated-interfaces</c>.
    /// </para>
    /// <para>Detection strategy:</para>
    /// <list type="bullet">
    /// <item>If the legacy type is present in the compilation (via the 1.5.378 stack or the shim
    /// package), fire when an <c>[Obsolete]</c>-marked type with the matching full name is
    /// referenced.</item>
    /// <item>If the legacy type has been genuinely removed (consumer is on the bare 2.0 stack
    /// and the call site no longer compiles), fall back to a match on the bare identifier name,
    /// scoped to source files that import any <c>Opc.Ua</c> namespace (bare
    /// <c>using Opc.Ua;</c> or any sub-namespace such as <c>using Opc.Ua.Server;</c>) or that
    /// declare a namespace under the <c>Opc.Ua</c> tree. Each candidate identifier is then
    /// bound semantically and is only reported when:
    /// <list type="bullet">
    /// <item>it does not bind at all (the legacy type is gone, so a legacy usage is
    /// unresolved);</item>
    /// <item>it is used in a type-only position (for example <c>typeof(CertificateValidator)</c>)
    /// but only a non-type member with that name is in scope; or</item>
    /// <item>it binds to a type (or a constructor of a type) declared under the <c>Opc.Ua</c>
    /// namespace tree.</item>
    /// </list>
    /// Identifiers that bind to members or other non-type symbols (for example a property
    /// <c>public ICertificateValidatorEx CertificateValidator { get; }</c>, or a field, local,
    /// parameter or method with that name) and identifiers that bind to a consumer's own type
    /// outside the <c>Opc.Ua</c> tree are not reported.</item>
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
            [DiagnosticDescriptors.UA0021_CertificateValidatorRename];

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
            var identifier = (IdentifierNameSyntax)context.Node;
            string text = identifier.Identifier.ValueText;
            if (text is not CertificateValidatorTypeName and not CertificateValidationEventArgsTypeName)
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
                (legacyValidator is not null &&
                    SymbolEqualityComparer.Default.Equals(namedType, legacyValidator)) ||
                (legacyEventArgs is not null &&
                    SymbolEqualityComparer.Default.Equals(namedType, legacyEventArgs));
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
            var identifier = (IdentifierNameSyntax)context.Node;
            string text = identifier.Identifier.ValueText;
            if (text is not CertificateValidatorTypeName and not CertificateValidationEventArgsTypeName)
            {
                return;
            }

            if (!HasOpcUaContext(identifier))
            {
                return;
            }

            SymbolInfo info = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken);
            if (!IsUnresolvedOrOpcUaType(info))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0021_CertificateValidatorRename,
                identifier.GetLocation(),
                text));
        }

        /// <summary>
        /// Decide whether a <c>CertificateValidator</c>/<c>CertificateValidationEventArgs</c>
        /// identifier, bound in a compilation where the legacy types are absent, still looks like
        /// a usage of the removed legacy type (see the type remarks for the exact rule).
        /// </summary>
        private static bool IsUnresolvedOrOpcUaType(SymbolInfo info)
        {
            if (info.Symbol is not null)
            {
                return IsOpcUaType(info.Symbol);
            }

            if (info.CandidateSymbols.IsDefaultOrEmpty ||
                info.CandidateReason == CandidateReason.NotATypeOrNamespace)
            {
                return true;
            }

            bool hasOpcUaType = false;
            foreach (ISymbol candidate in info.CandidateSymbols)
            {
                if (GetTypeOrConstructorType(candidate) is not INamedTypeSymbol)
                {
                    return false;
                }
                hasOpcUaType |= IsOpcUaType(candidate);
            }
            return hasOpcUaType;
        }

        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="symbol"/> is a named type (or a
        /// constructor of a named type) declared under the <c>Opc.Ua</c> namespace tree.
        /// </summary>
        private static bool IsOpcUaType(ISymbol symbol)
        {
            INamedTypeSymbol? type = GetTypeOrConstructorType(symbol);
            if (type is null)
            {
                return false;
            }
            string? ns = type.ContainingNamespace?.ToDisplayString();
            return ns is not null &&
                (string.Equals(ns, OpcUaNamespace, StringComparison.Ordinal) ||
                    ns.StartsWith(OpcUaNamespace + ".", StringComparison.Ordinal));
        }

        /// <summary>
        /// Returns the named type itself, the containing type of a constructor, or
        /// <see langword="null"/> for any other symbol kind.
        /// </summary>
        private static INamedTypeSymbol? GetTypeOrConstructorType(ISymbol symbol)
        {
            return symbol switch
            {
                INamedTypeSymbol type => type,
                IMethodSymbol { MethodKind: MethodKind.Constructor } ctor => ctor.ContainingType,
                _ => null
            };
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
            return string.Equals(text, OpcUaNamespace, StringComparison.Ordinal) ||
                text.StartsWith(OpcUaNamespace + ".", StringComparison.Ordinal);
        }
    }
}
