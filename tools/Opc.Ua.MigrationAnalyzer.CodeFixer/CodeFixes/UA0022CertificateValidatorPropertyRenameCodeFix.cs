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
    /// UA0022 code fix: rewrite <c>xxx.CertificateValidator</c> as
    /// <c>xxx.CertificateManager</c>. Only the property identifier is
    /// renamed; downstream member access on the (now differently-typed)
    /// <c>ICertificateManager</c> result may still need manual review.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0022CertificateValidatorPropertyRenameCodeFix))]
    [Shared]
    public sealed class UA0022CertificateValidatorPropertyRenameCodeFix : CodeFixProvider
    {
        private const string NewPropertyName = "CertificateManager";

        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0022];

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
                MemberAccessExpressionSyntax memberAccess = node.AncestorsAndSelf()
                    .OfType<MemberAccessExpressionSyntax>()
                    .FirstOrDefault();
                if (memberAccess is null ||
                    memberAccess.Name.Identifier.ValueText != "CertificateValidator")
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Use 'CertificateManager' property",
                        createChangedDocument: ct => ApplyAsync(context.Document, memberAccess, ct),
                        equivalenceKey: DiagnosticIds.UA0022),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            MemberAccessExpressionSyntax memberAccess,
            CancellationToken cancellationToken)
        {
            SyntaxToken oldIdentifier = memberAccess.Name.Identifier;
            SyntaxToken newIdentifier = SyntaxFactory.Identifier(
                oldIdentifier.LeadingTrivia,
                NewPropertyName,
                oldIdentifier.TrailingTrivia);
            MemberAccessExpressionSyntax newMemberAccess = memberAccess
                .WithName(SyntaxFactory.IdentifierName(newIdentifier));

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(memberAccess, newMemberAccess);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
