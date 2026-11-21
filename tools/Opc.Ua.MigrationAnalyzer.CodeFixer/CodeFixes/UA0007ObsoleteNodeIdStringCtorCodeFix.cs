// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// UA0007 code fix: rewrite <c>new NodeId(s)</c> / <c>new ExpandedNodeId(s)</c>
    /// as the corresponding <c>Parse(s)</c> call.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0007ObsoleteNodeIdStringCtorCodeFix))]
    [Shared]
    public sealed class UA0007ObsoleteNodeIdStringCtorCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0007];

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
