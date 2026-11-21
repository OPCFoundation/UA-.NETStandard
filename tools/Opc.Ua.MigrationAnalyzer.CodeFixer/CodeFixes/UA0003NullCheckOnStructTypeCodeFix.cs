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
    /// UA0003 code fix: rewrite <c>x == null</c> as <c>x.IsNull</c> (or
    /// <c>x.IsNullOrEmpty</c> for LocalizedText) and <c>x != null</c> as the
    /// negated form.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0003NullCheckOnStructTypeCodeFix))]
    [Shared]
    public sealed class UA0003NullCheckOnStructTypeCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0003];

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
                BinaryExpressionSyntax binary = node.AncestorsAndSelf()
                    .OfType<BinaryExpressionSyntax>()
                    .FirstOrDefault(b => b.IsKind(SyntaxKind.EqualsExpression) || b.IsKind(SyntaxKind.NotEqualsExpression));
                if (binary is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Use '.IsNull' instead of null comparison",
                        createChangedDocument: ct => ApplyAsync(context.Document, binary, ct),
                        equivalenceKey: DiagnosticIds.UA0003),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            BinaryExpressionSyntax binary,
            CancellationToken cancellationToken)
        {
            SemanticModel model = (await document.GetSemanticModelAsync(cancellationToken)
                .ConfigureAwait(false))!;

            ExpressionSyntax valueExpr = GetValueExpression(binary);
            if (valueExpr is null)
            {
                return document;
            }

            ITypeSymbol valueType = model.GetTypeInfo(valueExpr, cancellationToken).Type;
            if (valueType is INamedTypeSymbol nullable &&
                nullable.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T &&
                nullable.TypeArguments.Length == 1)
            {
                valueType = nullable.TypeArguments[0];
            }
            string memberName = valueType?.Name == "LocalizedText" ? "IsNullOrEmpty" : "IsNull";

            MemberAccessExpressionSyntax access = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                valueExpr.WithoutTrivia(),
                SyntaxFactory.IdentifierName(memberName));

            ExpressionSyntax replacement = binary.IsKind(SyntaxKind.EqualsExpression)
                ? access
                : SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, access);

            replacement = replacement.WithTriviaFrom(binary);

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(binary, replacement);
            return document.WithSyntaxRoot(newRoot);
        }

        private static ExpressionSyntax GetValueExpression(BinaryExpressionSyntax binary)
        {
            if (binary.Left.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return binary.Right;
            }
            if (binary.Right.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return binary.Left;
            }
            return null;
        }
    }
}
