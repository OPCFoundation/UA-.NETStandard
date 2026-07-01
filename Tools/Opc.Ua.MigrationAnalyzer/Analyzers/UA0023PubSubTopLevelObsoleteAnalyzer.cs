/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.Analyzers
{
    /// <summary>
    /// UA0023: Detect references to the legacy 1.04 OPC UA PubSub
    /// top-level types (now obsolete shims in 2.0) and recommend the
    /// new <c>IPubSubApplication</c> / <c>PubSubApplicationBuilder</c>
    /// surface — or the
    /// <c>Microsoft.Extensions.DependencyInjection.AddPubSub()</c>
    /// entry point.
    /// </summary>
    /// <remarks>
    /// The following symbols trigger the rule when referenced from
    /// consumer code:
    /// <list type="bullet">
    ///   <item><c>Opc.Ua.PubSub.UaPubSubApplication</c></item>
    ///   <item><c>Opc.Ua.PubSub.IUaPubSubConnection</c></item>
    ///   <item><c>Opc.Ua.PubSub.UaPubSubConnection</c></item>
    ///   <item><c>Opc.Ua.PubSub.IUaPublisher</c></item>
    ///   <item><c>Opc.Ua.PubSub.UaPublisher</c></item>
    ///   <item><c>Opc.Ua.PubSub.IUaPubSubDataStore</c></item>
    ///   <item><c>Opc.Ua.PubSub.UaPubSubDataStore</c></item>
    ///   <item><c>Opc.Ua.PubSub.Configuration.UaPubSubConfigurator</c></item>
    /// </list>
    /// Detection is dual-mode like UA0021 / UA0022 — semantic path
    /// for the still-shipped types, plus a syntactic fallback for the
    /// (rare) case where the legacy assembly is no longer referenced.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UA0023PubSubTopLevelObsoleteAnalyzer : DiagnosticAnalyzer
    {
        private const string PubSubNamespace = "Opc.Ua.PubSub";
        private const string ConfigurationNamespace = "Opc.Ua.PubSub.Configuration";

        private static readonly ImmutableHashSet<string> s_legacyShortNames =
        [
            "UaPubSubApplication",
            "IUaPubSubConnection",
            "UaPubSubConnection",
            "IUaPublisher",
            "UaPublisher",
            "IUaPubSubDataStore",
            "UaPubSubDataStore",
            "UaPubSubConfigurator",
        ];

        private static readonly ImmutableHashSet<string> s_legacyFullNames =
        [
            PubSubNamespace + ".UaPubSubApplication",
            PubSubNamespace + ".IUaPubSubConnection",
            PubSubNamespace + ".UaPubSubConnection",
            PubSubNamespace + ".IUaPublisher",
            PubSubNamespace + ".UaPublisher",
            PubSubNamespace + ".IUaPubSubDataStore",
            PubSubNamespace + ".UaPubSubDataStore",
            ConfigurationNamespace + ".UaPubSubConfigurator",
        ];

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            [DiagnosticDescriptors.UA0023_PubSubTopLevelObsolete];

        public override void Initialize(AnalysisContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(static startContext =>
            {
                Dictionary<string, INamedTypeSymbol> resolved = new(StringComparer.Ordinal);
                foreach (string fullName in s_legacyFullNames)
                {
                    INamedTypeSymbol? sym =
                        startContext.Compilation.GetTypeByMetadataName(fullName);
                    if (sym is not null)
                    {
                        resolved[fullName] = sym;
                    }
                }

                startContext.RegisterSyntaxNodeAction(
                    ctx => AnalyzeIdentifier(ctx, resolved),
                    SyntaxKind.IdentifierName);
            });
        }

        private static void AnalyzeIdentifier(
            SyntaxNodeAnalysisContext context,
            Dictionary<string, INamedTypeSymbol> resolvedTypes)
        {
            var identifier = (IdentifierNameSyntax)context.Node;
            string name = identifier.Identifier.ValueText;
            if (!s_legacyShortNames.Contains(name))
            {
                return;
            }

            // Skip identifier appearing on the right of a member access
            // ("foo.UaPubSubApplication") — only the left of a member access
            // or a bare identifier is a type reference.
            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name == identifier)
            {
                return;
            }

            SymbolInfo info = context.SemanticModel
                .GetSymbolInfo(identifier, context.CancellationToken);
            ISymbol? symbol = info.Symbol;
            if (symbol is INamedTypeSymbol resolvedType)
            {
                string fullName = resolvedType.ToDisplayString();
                if (!s_legacyFullNames.Contains(fullName))
                {
                    return;
                }
                Report(context, identifier, name);
                return;
            }

            // Error symbols / fallback path — be lenient and report when the
            // short name matches and the file imports an Opc.Ua.PubSub
            // namespace.
            if (symbol is null && HasOpcUaPubSubUsing(identifier))
            {
                Report(context, identifier, name);
            }
        }

        private static void Report(
            SyntaxNodeAnalysisContext context,
            SyntaxNode location,
            string typeName)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UA0023_PubSubTopLevelObsolete,
                location.GetLocation(),
                typeName));
        }

        private static bool HasOpcUaPubSubUsing(SyntaxNode node)
        {
            for (SyntaxNode? current = node; current is not null; current = current.Parent)
            {
                if (current is BaseNamespaceDeclarationSyntax ns
                    && ContainsPubSubUsing(ns.Usings))
                {
                    return true;
                }
                if (current is CompilationUnitSyntax compilationUnit)
                {
                    return ContainsPubSubUsing(compilationUnit.Usings);
                }
            }
            return false;
        }

        private static bool ContainsPubSubUsing(SyntaxList<UsingDirectiveSyntax> usings)
        {
            foreach (UsingDirectiveSyntax @using in usings)
            {
                if (@using.Alias is not null
                    || @using.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                {
                    continue;
                }
                if (@using.Name is null)
                {
                    continue;
                }
                string text = @using.Name.ToString();
                if (string.Equals(text, PubSubNamespace, StringComparison.Ordinal)
                    || text.StartsWith(PubSubNamespace + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
