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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Opc.Ua.MigrationAnalyzer.Diagnostics;

namespace Opc.Ua.MigrationAnalyzer.CodeFixer
{
    /// <summary>
    /// UA0014 code fix: rewrite <c>DataValue.IsGood(dv)</c> (or the
    /// <c>DataValueExtensions</c> extension form) as <c>dv.IsGood</c>.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0014DataValueIsGoodCodeFix))]
    [Shared]
    public sealed class UA0014DataValueIsGoodCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0014];

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
