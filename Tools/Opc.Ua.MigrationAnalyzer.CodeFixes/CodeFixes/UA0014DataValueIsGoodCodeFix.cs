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
    /// UA0014 code fix: rewrite <c>DataValue.IsGood(dv)</c> (or the
    /// <c>DataValueExtensions</c> extension form) as <c>dv.IsGood</c>.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0014DataValueIsGoodCodeFix)), Shared]
    public sealed class UA0014DataValueIsGoodCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(DiagnosticIds.UA0014);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false))!;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                InvocationExpressionSyntax invocation = node.AncestorsAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault();
                if (invocation is null || invocation.ArgumentList.Arguments.Count != 1)
                {
                    continue;
                }

                string memberName = GetMemberName(invocation);
                if (memberName is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: $"Use '{memberName}' instance property",
                        createChangedDocument: ct => ApplyAsync(context.Document, invocation, memberName, ct),
                        equivalenceKey: $"{DiagnosticIds.UA0014}:{memberName}"),
                    diagnostic);
            }
        }

        private static string GetMemberName(InvocationExpressionSyntax invocation)
        {
            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax member:
                    return member.Name.Identifier.ValueText;
                case IdentifierNameSyntax id:
                    return id.Identifier.ValueText;
                default:
                    return null;
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            InvocationExpressionSyntax invocation,
            string memberName,
            CancellationToken cancellationToken)
        {
            ArgumentSyntax arg = invocation.ArgumentList.Arguments[0];
            ExpressionSyntax receiver = arg.Expression.WithoutTrivia();

            MemberAccessExpressionSyntax replacement = SyntaxFactory
                .MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    SyntaxFactory.IdentifierName(memberName))
                .WithTriviaFrom(invocation);

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(invocation, replacement);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
