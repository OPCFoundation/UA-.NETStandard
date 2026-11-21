// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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

namespace Opc.Ua.MigrationAnalyzer.CodeFixer
{
    /// <summary>
    /// UA0008 code fix: wrap each variadic argument of <c>ISession.Call</c> /
    /// <c>ISession.CallAsync</c> with <c>Variant.From(...)</c> (replacing
    /// <c>null</c> literals with <c>Variant.Null</c>).
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0008SessionCallParamsObjectCodeFix))]
    [Shared]
    public sealed class UA0008SessionCallParamsObjectCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0008];

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
                if (invocation is null || invocation.ArgumentList is null)
                {
                    continue;
                }

                if (!diagnostic.Properties.TryGetValue(
                    WellKnownProperties.MethodName,
                    out string methodName))
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Wrap arguments with 'Variant.From(...)'",
                        createChangedDocument: ct => ApplyAsync(context.Document, invocation, methodName, ct),
                        equivalenceKey: DiagnosticIds.UA0008),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            InvocationExpressionSyntax invocation,
            string methodName,
            CancellationToken cancellationToken)
        {
            int firstVariadicIndex = methodName == "Call" ? 2 : 3;
            SeparatedSyntaxList<ArgumentSyntax> originalArgs = invocation.ArgumentList.Arguments;

            var newArgs = new List<ArgumentSyntax>(originalArgs.Count);
            for (int i = 0; i < originalArgs.Count; i++)
            {
                ArgumentSyntax arg = originalArgs[i];
                if (i < firstVariadicIndex)
                {
                    newArgs.Add(arg);
                    continue;
                }
                newArgs.Add(arg.WithExpression(Wrap(arg.Expression)));
            }

            InvocationExpressionSyntax newInvocation = invocation
                .WithArgumentList(invocation.ArgumentList.WithArguments(
                    SyntaxFactory.SeparatedList(newArgs)));

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(invocation, newInvocation);
            return document.WithSyntaxRoot(newRoot);
        }

        private static ExpressionSyntax Wrap(ExpressionSyntax expression)
        {
            if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("Variant"),
                    SyntaxFactory.IdentifierName("Null"))
                    .WithTriviaFrom(expression);
            }

            ExpressionSyntax stripped = expression.WithoutTrivia();
            InvocationExpressionSyntax call = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("Variant"),
                    SyntaxFactory.IdentifierName("From")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(stripped))));
            return call.WithTriviaFrom(expression);
        }
    }
}
