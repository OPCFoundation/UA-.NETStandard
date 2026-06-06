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
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.CodeFixer
{
    /// <summary>
    /// UA0004 code fix: drop the leading <c>?.</c> of a null-conditional chain
    /// whose receiver is a now-struct type, leaving any deeper <c>?.</c> intact.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0004ConditionalAccessOnStructCodeFix)), Shared]
    public sealed class UA0004ConditionalAccessOnStructCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(DiagnosticIds.UA0004);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false))!;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                ConditionalAccessExpressionSyntax condAccess = node.AncestorsAndSelf()
                    .OfType<ConditionalAccessExpressionSyntax>()
                    .FirstOrDefault();
                if (condAccess is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Drop '?.' on value-type receiver",
                        createChangedDocument: ct => ApplyAsync(context.Document, condAccess, ct),
                        equivalenceKey: DiagnosticIds.UA0004),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            ConditionalAccessExpressionSyntax condAccess,
            CancellationToken cancellationToken)
        {
            ExpressionSyntax receiver = condAccess.Expression;
            ExpressionSyntax whenNotNull = condAccess.WhenNotNull;

            MemberBindingExpressionSyntax firstBinding = whenNotNull
                .DescendantNodesAndSelf()
                .OfType<MemberBindingExpressionSyntax>()
                .FirstOrDefault();
            if (firstBinding is null)
            {
                return document;
            }

            MemberAccessExpressionSyntax memberAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver.WithoutTrivia(),
                firstBinding.Name);

            ExpressionSyntax replacement;
            if (firstBinding == whenNotNull)
            {
                replacement = memberAccess;
            }
            else
            {
                replacement = whenNotNull.ReplaceNode(firstBinding, memberAccess);
            }
            replacement = replacement.WithTriviaFrom(condAccess);

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(condAccess, replacement);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
