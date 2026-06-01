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
    /// UA0009 code fix: rewrite
    /// <c>[DataContract]</c>/<c>[DataMember]</c> attributes to the OPC UA 2.0
    /// <c>[DataType]</c>/<c>[DataTypeField]</c> equivalents, mark the class
    /// <c>partial</c>, and add <c>using Opc.Ua;</c> when missing.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UA0009DataContractToDataTypeCodeFix)), Shared]
    public sealed class UA0009DataContractToDataTypeCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(DiagnosticIds.UA0009);

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            SyntaxNode root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false))!;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                SyntaxNode node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
                ClassDeclarationSyntax classDecl = node.AncestorsAndSelf()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault();
                if (classDecl is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Migrate to [DataType]/[DataTypeField] and add 'partial'",
                        createChangedDocument: ct => ApplyAsync(context.Document, classDecl, ct),
                        equivalenceKey: DiagnosticIds.UA0009),
                    diagnostic);
            }
        }

        private static async Task<Document> ApplyAsync(
            Document document,
            ClassDeclarationSyntax classDecl,
            CancellationToken cancellationToken)
        {
            SyntaxNode root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;

            ClassDeclarationSyntax rewritten = RewriteClass(classDecl);
            SyntaxNode newRoot = root.ReplaceNode(classDecl, rewritten);

            if (newRoot is CompilationUnitSyntax compilationUnit)
            {
                newRoot = EnsureUsing(compilationUnit, "Opc.Ua");
            }

            return document.WithSyntaxRoot(newRoot);
        }

        private static ClassDeclarationSyntax RewriteClass(ClassDeclarationSyntax classDecl)
        {
            SyntaxList<AttributeListSyntax> newAttributeLists = RewriteAttributeLists(classDecl.AttributeLists);
            ClassDeclarationSyntax result = classDecl.WithAttributeLists(newAttributeLists);

            if (!result.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                SyntaxToken partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space);
                result = result.WithModifiers(result.Modifiers.Add(partialToken));
            }

            List<MemberDeclarationSyntax> newMembers = new List<MemberDeclarationSyntax>(result.Members.Count);
            foreach (MemberDeclarationSyntax member in result.Members)
            {
                if (member is PropertyDeclarationSyntax property)
                {
                    SyntaxList<AttributeListSyntax> rewritten = RewriteAttributeLists(property.AttributeLists);
                    newMembers.Add(property.WithAttributeLists(rewritten));
                }
                else
                {
                    newMembers.Add(member);
                }
            }
            return result.WithMembers(SyntaxFactory.List(newMembers));
        }

        private static SyntaxList<AttributeListSyntax> RewriteAttributeLists(
            SyntaxList<AttributeListSyntax> attributeLists)
        {
            List<AttributeListSyntax> newLists = new List<AttributeListSyntax>(attributeLists.Count);
            foreach (AttributeListSyntax list in attributeLists)
            {
                List<AttributeSyntax> newAttrs = new List<AttributeSyntax>(list.Attributes.Count);
                foreach (AttributeSyntax attr in list.Attributes)
                {
                    newAttrs.Add(RewriteAttribute(attr));
                }
                newLists.Add(list.WithAttributes(SyntaxFactory.SeparatedList(newAttrs)));
            }
            return SyntaxFactory.List(newLists);
        }

        private static AttributeSyntax RewriteAttribute(AttributeSyntax attribute)
        {
            string simpleName = GetSimpleAttributeName(attribute.Name);
            if (simpleName == "DataContract" || simpleName == "DataContractAttribute")
            {
                return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("DataType"))
                    .WithTriviaFrom(attribute);
            }
            if (simpleName == "DataMember" || simpleName == "DataMemberAttribute")
            {
                AttributeArgumentListSyntax argList = FilterToOrderArgument(attribute.ArgumentList);
                AttributeSyntax replacement = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("DataTypeField"));
                if (argList != null)
                {
                    replacement = replacement.WithArgumentList(argList);
                }
                return replacement.WithTriviaFrom(attribute);
            }
            return attribute;
        }

        private static string GetSimpleAttributeName(NameSyntax name)
        {
            switch (name)
            {
                case IdentifierNameSyntax id:
                    return id.Identifier.ValueText;
                case QualifiedNameSyntax qn:
                    return qn.Right.Identifier.ValueText;
                case AliasQualifiedNameSyntax aq:
                    return aq.Name.Identifier.ValueText;
                default:
                    return name?.ToString();
            }
        }

        private static AttributeArgumentListSyntax FilterToOrderArgument(AttributeArgumentListSyntax argumentList)
        {
            if (argumentList is null)
            {
                return null;
            }
            List<AttributeArgumentSyntax> kept = new List<AttributeArgumentSyntax>();
            foreach (AttributeArgumentSyntax arg in argumentList.Arguments)
            {
                if (arg.NameEquals?.Name.Identifier.ValueText == "Order")
                {
                    kept.Add(arg.WithoutTrivia());
                }
            }
            if (kept.Count == 0)
            {
                return null;
            }
            return SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(kept));
        }

        private static CompilationUnitSyntax EnsureUsing(CompilationUnitSyntax compilationUnit, string namespaceName)
        {
            foreach (UsingDirectiveSyntax existing in compilationUnit.Usings)
            {
                if (existing.Name?.ToString() == namespaceName)
                {
                    return compilationUnit;
                }
            }

            UsingDirectiveSyntax newUsing = SyntaxFactory.UsingDirective(
                SyntaxFactory.ParseName(namespaceName));
            return compilationUnit.AddUsings(newUsing);
        }
    }
}
