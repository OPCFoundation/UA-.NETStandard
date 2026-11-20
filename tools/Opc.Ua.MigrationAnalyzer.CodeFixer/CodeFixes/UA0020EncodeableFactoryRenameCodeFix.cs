// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.CodeFixer
{
    /// <summary>
    /// UA0020 code fix: rewrite instance <c>factory.Create()</c> as
    /// <c>factory.Fork()</c>. The <c>GlobalFactory</c> form has no
    /// automatic fix because the replacement
    /// (<c>ServiceMessageContext.Factory</c>) requires a context instance
    /// that the analyzer cannot conjure.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0020EncodeableFactoryRenameCodeFix))]
    [Shared]
    public sealed class UA0020EncodeableFactoryRenameCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0020];

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
                if (!diagnostic.Properties.TryGetValue(
                    WellKnownProperties.Form,
                    out string form) ||
                    form != WellKnownProperties.FormCreate)
                {
                    continue;
                }

                SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                InvocationExpressionSyntax invocation = node.AncestorsAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault();
                if (invocation is null || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Use 'Fork()'",
                        createChangedDocument: ct => ApplyAsync(context.Document, invocation, memberAccess, ct),
                        equivalenceKey: DiagnosticIds.UA0020),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            InvocationExpressionSyntax invocation,
            MemberAccessExpressionSyntax memberAccess,
            CancellationToken cancellationToken)
        {
            MemberAccessExpressionSyntax newMemberAccess = memberAccess
                .WithName(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName("Fork"));
            InvocationExpressionSyntax newInvocation = invocation.WithExpression(newMemberAccess);

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(invocation, newInvocation);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
