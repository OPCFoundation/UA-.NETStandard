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

namespace Opc.Ua.MigrationAnalyzer.CodeFixer
{
    /// <summary>
    /// UA0006 code fix: rewrite obsolete <c>new Variant(...)</c> as
    /// <c>Variant.From(...)</c>, wrapping DateTime/Guid/byte[] arguments with
    /// the appropriate strongly-typed surface (DateTimeUtc, Uuid, ByteString).
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0006ObsoleteVariantCtorCodeFix))]
    [Shared]
    public sealed class UA0006ObsoleteVariantCtorCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            [DiagnosticIds.UA0006];

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
                if (creation is null ||
                    creation.ArgumentList is null ||
                    creation.ArgumentList.Arguments.Count != 1)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Use 'Variant.From(...)'",
                        createChangedDocument: ct => ApplyAsync(context.Document, creation, ct),
                        equivalenceKey: DiagnosticIds.UA0006),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            ObjectCreationExpressionSyntax creation,
            CancellationToken cancellationToken)
        {
            SemanticModel model = (await document.GetSemanticModelAsync(cancellationToken)
                .ConfigureAwait(false))!;

            ArgumentSyntax arg = creation.ArgumentList!.Arguments[0];
            ITypeSymbol argType = model.GetTypeInfo(arg.Expression, cancellationToken).Type;
            ExpressionSyntax inner = BuildInner(arg.Expression.WithoutTrivia(), argType);

            InvocationExpressionSyntax replacement = SyntaxFactory
                .InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("Variant"),
                        SyntaxFactory.IdentifierName("From")),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(inner))))
                .WithTriviaFrom(creation);

            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            SyntaxNode newRoot = root.ReplaceNode(creation, replacement);
            return document.WithSyntaxRoot(newRoot);
        }

        private static ExpressionSyntax BuildInner(ExpressionSyntax argExpr, ITypeSymbol argType)
        {
            if (argType is null)
            {
                return argExpr;
            }
            switch (argType.SpecialType)
            {
                case SpecialType.System_DateTime:
                    return SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.IdentifierName("DateTimeUtc"),
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(argExpr))),
                        initializer: null);
            }
            if (argType.ToDisplayString() == "System.Guid")
            {
                return SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.IdentifierName("Uuid"),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(argExpr))),
                    initializer: null);
            }
            if (argType is IArrayTypeSymbol arr &&
                arr.ElementType?.SpecialType == SpecialType.System_Byte)
            {
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        argExpr,
                        SyntaxFactory.IdentifierName("ToByteString")));
            }
            return argExpr;
        }
    }
}
