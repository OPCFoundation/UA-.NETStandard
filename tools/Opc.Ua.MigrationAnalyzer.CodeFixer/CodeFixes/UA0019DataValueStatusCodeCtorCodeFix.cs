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
    /// UA0019 code fix: rewrite <c>new DataValue(sc)</c> / <c>new DataValue(sc, ts)</c>
    /// as <c>DataValue.FromStatusCode(sc)</c> / <c>DataValue.FromStatusCode(sc, ts)</c>.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0019DataValueStatusCodeCtorCodeFix))]
    [Shared]
    public sealed class UA0019DataValueStatusCodeCtorCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0019];

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
                ObjectCreationExpressionSyntax creation = node.AncestorsAndSelf()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .FirstOrDefault();
                if (creation is null || creation.ArgumentList is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Use 'DataValue.FromStatusCode(...)'",
                        createChangedDocument: ct => ApplyAsync(context.Document, creation, ct),
                        equivalenceKey: DiagnosticIds.UA0019),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            ObjectCreationExpressionSyntax creation,
            CancellationToken cancellationToken)
        {
            InvocationExpressionSyntax replacement = SyntaxFactory
                .InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("DataValue"),
                        SyntaxFactory.IdentifierName("FromStatusCode")),
                    creation.ArgumentList!)
                .WithTriviaFrom(creation);

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(creation, replacement);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
