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

namespace Opc.Ua.MigrationAnalyzer.CodeFixes
{
    /// <summary>
    /// UA0002 code fix: rewrite every reference to a removed
    /// <c>&lt;Type&gt;Collection</c> wrapper as <c>List&lt;TElement&gt;</c>.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0002RemovedCollectionTypeCodeFix)), Shared]
    public sealed class UA0002RemovedCollectionTypeCodeFix : CodeFixProvider
    {
        private static readonly Dictionary<string, string> s_shortNameToElement = BuildShortNameMap();

        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(DiagnosticIds.UA0002);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

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
            CollectionRewriter rewriter = new CollectionRewriter();
            SyntaxNode newRoot = rewriter.Visit(root);
            return document.WithSyntaxRoot(newRoot);
        }

        private static Dictionary<string, string> BuildShortNameMap()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach ((string collectionName, string elementName) in SymbolExtensions.RemovedCollectionTypes)
            {
                string shortCollection = StripNamespace(collectionName);
                string shortElement = StripNamespace(elementName);
                map[shortCollection] = shortElement;
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
                            SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                SyntaxFactory.ParseTypeName(elementName))));
                    return replacement.WithTriviaFrom(node);
                }
                return base.VisitIdentifierName(node);
            }
        }
    }
}
