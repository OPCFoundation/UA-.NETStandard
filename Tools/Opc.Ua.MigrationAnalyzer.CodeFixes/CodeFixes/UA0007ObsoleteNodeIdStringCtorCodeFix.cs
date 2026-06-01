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

namespace Opc.Ua.MigrationAnalyzer.CodeFixes
{
    /// <summary>
    /// UA0007 code fix: rewrite <c>new NodeId(s)</c> / <c>new ExpandedNodeId(s)</c>
    /// as the corresponding <c>Parse(s)</c> call.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0007ObsoleteNodeIdStringCtorCodeFix)), Shared]
    public sealed class UA0007ObsoleteNodeIdStringCtorCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(DiagnosticIds.UA0007);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false))!;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                ObjectCreationExpressionSyntax creation = node.AncestorsAndSelf()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .FirstOrDefault();
                if (creation is null || creation.ArgumentList is null)
                {
                    continue;
                }

                string typeName = GetTypeName(creation);
                if (typeName is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Use '{typeName}.Parse(s)'",
                        createChangedDocument: ct => ApplyAsync(context.Document, creation, typeName, ct),
                        equivalenceKey: $"{DiagnosticIds.UA0007}:{typeName}"),
                    diagnostic);
            }
        }

        private static string GetTypeName(ObjectCreationExpressionSyntax creation)
        {
            switch (creation.Type)
            {
                case IdentifierNameSyntax id:
                    return id.Identifier.ValueText;
                case QualifiedNameSyntax qn:
                    return qn.Right.Identifier.ValueText;
                default:
                    return null;
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            ObjectCreationExpressionSyntax creation,
            string typeName,
            CancellationToken cancellationToken)
        {
            InvocationExpressionSyntax replacement = SyntaxFactory
                .InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(typeName),
                        SyntaxFactory.IdentifierName("Parse")),
                    creation.ArgumentList!)
                .WithTriviaFrom(creation);

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(creation, replacement);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
