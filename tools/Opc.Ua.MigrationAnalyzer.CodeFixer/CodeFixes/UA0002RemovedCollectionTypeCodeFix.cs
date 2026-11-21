// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Opc.Ua.MigrationAnalyzer.Diagnostics;
using Opc.Ua.MigrationAnalyzer.Helpers;

namespace Opc.Ua.MigrationAnalyzer.CodeFixer
{
    /// <summary>
    /// UA0002 code fix: rewrite every reference to a removed
    /// <c>&lt;Type&gt;Collection</c> wrapper as <c>List&lt;TElement&gt;</c>.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0002RemovedCollectionTypeCodeFix))]
    [Shared]
    public sealed class UA0002RemovedCollectionTypeCodeFix : CodeFixProvider
    {
        private static readonly Dictionary<string, string> s_shortNameToElement = BuildShortNameMap();

        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0002];

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Use 'List<T>'",
                        createChangedDocument: ct => ApplyAsync(context.Document, ct),
                        equivalenceKey: DiagnosticIds.UA0002),
                    diagnostic);
            }
            return Task.CompletedTask;
        }

        private static async Task<Document> ApplyAsync(Document document, CancellationToken cancellationToken)
        {
            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var rewriter = new CollectionRewriter();
            SyntaxNode newRoot = rewriter.Visit(root);
            return document.WithSyntaxRoot(newRoot);
        }

        private static Dictionary<string, string> BuildShortNameMap()
        {
            Dictionary<string, string> map = [];
            foreach ((string collectionName, string elementName) in SymbolExtensions.RemovedCollectionTypes)
            {
                string shortCollection = StripNamespace(collectionName);
                map[shortCollection] = StripNamespace(elementName);
            }
            return map;
        }

        private static string StripNamespace(string name)
        {
            int lastDot = name.LastIndexOf('.');
            return lastDot < 0 ? name : name.Substring(lastDot + 1);
        }

        private sealed class CollectionRewriter : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (s_shortNameToElement.TryGetValue(node.Identifier.ValueText, out string elementName))
                {
                    GenericNameSyntax replacement = SyntaxFactory.GenericName(
                        SyntaxFactory.Identifier("List"),
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.ParseTypeName(elementName))));
                    return replacement.WithTriviaFrom(node);
                }
                return base.VisitIdentifierName(node);
            }
        }
    }
}
