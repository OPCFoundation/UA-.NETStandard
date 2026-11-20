/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
    /// UA0005 code fix: append <c>.ToByteString()</c> to a <c>byte[]</c>
    /// argument that needs to become a <c>ByteString</c>.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0005ByteArrayToByteStringCodeFix))]
    [Shared]
    public sealed class UA0005ByteArrayToByteStringCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0005];

        public override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false))!;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                ArgumentSyntax argument = node.AncestorsAndSelf()
                    .OfType<ArgumentSyntax>()
                    .FirstOrDefault();
                if (argument is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Append '.ToByteString()'",
                        createChangedDocument: ct => ApplyAsync(context.Document, argument, ct),
                        equivalenceKey: DiagnosticIds.UA0005),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            ArgumentSyntax argument,
            CancellationToken cancellationToken)
        {
            ExpressionSyntax expr = argument.Expression;

            InvocationExpressionSyntax newInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    expr.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("ToByteString")));

            ArgumentSyntax newArgument = argument.WithExpression(newInvocation);

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(argument, newArgument);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
